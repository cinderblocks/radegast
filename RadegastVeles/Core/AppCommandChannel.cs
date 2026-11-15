/*
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */


using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LibreMetaverse;

namespace Radegast.Veles.Core;

/// <summary>
/// Carries an <see cref="AppCommand"/> from a newly started copy of the program to the copy
/// already running. The running copy listens on a named pipe (Windows) or a Unix socket in the
/// application data directory (elsewhere); both are reachable only by the same user. The socket
/// is a file rather than a .NET named pipe because those live under /tmp, which Flatpak does not
/// share between two runs of the same application.
/// </summary>
internal sealed class AppCommandChannel : IDisposable
{
    private const int MaxLineBytes = 256;
    private const int IoTimeoutMs = 2000;
    private const int ConnectTimeoutMs = 1000;
    // sockaddr_un holds 104 bytes on macOS and 108 on Linux, including the terminator.
    private const int MaxSocketPathBytes = 100;
    private const string Accepted = "ok";

    private readonly CancellationTokenSource _cts = new();
    private readonly Socket? _listener;
    private readonly string _endpoint;

    /// <summary>The endpoint for this user: a pipe name on Windows, a socket path elsewhere.</summary>
    public static string DefaultEndpoint()
    {
        if (OperatingSystem.IsWindows())
        {
            var user = new StringBuilder();
            foreach (char c in Environment.UserName)
                user.Append(char.IsLetterOrDigit(c) ? c : '_');
            return $"RadegastVeles.commands.{user}";
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RadegastVeles", "veles.sock");
    }

    /// <summary>
    /// Sends a command to the running copy. Returns true only if a running copy accepted it.
    /// </summary>
    public static bool TrySend(string endpoint, AppCommand command)
    {
        try
        {
            using var stream = Connect(endpoint);
            if (stream == null) return false;

            // Windows only lets the foreground process hand the foreground to another one, and
            // the running copy needs it to raise a window in response.
            if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(AnyProcess);

            using var timeout = new CancellationTokenSource(IoTimeoutMs);
            stream.WriteAsync(Encoding.UTF8.GetBytes(command.ToArguments() + "\n"), timeout.Token)
                .AsTask().GetAwaiter().GetResult();
            stream.FlushAsync(timeout.Token).GetAwaiter().GetResult();
            return ReadLineAsync(stream, timeout.Token).GetAwaiter().GetResult() == Accepted;
        }
        catch (Exception ex) when (ex is IOException or SocketException or TimeoutException
                                       or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts listening for commands. Returns null if another copy is already listening or the
    /// endpoint cannot be opened; this copy then runs without receiving commands.
    /// <paramref name="onCommand"/> is called on a thread-pool thread.
    /// </summary>
    public static AppCommandChannel? TryListen(string endpoint, Action<AppCommand> onCommand)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var first = CreatePipe(endpoint, firstInstance: true);
                return new AppCommandChannel(endpoint, null, first, onCommand);
            }

            if (Encoding.UTF8.GetByteCount(endpoint) > MaxSocketPathBytes)
            {
                Logger.Warn($"The command socket path is too long to use: {endpoint}");
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(endpoint)!);
            if (File.Exists(endpoint))
            {
                using var probe = Connect(endpoint);
                if (probe != null) return null;
                File.Delete(endpoint);
            }

            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                listener.Bind(new UnixDomainSocketEndPoint(endpoint));
                File.SetUnixFileMode(endpoint, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                listener.Listen(4);
            }
            catch
            {
                listener.Dispose();
                throw;
            }
            return new AppCommandChannel(endpoint, listener, null, onCommand);
        }
        catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException)
        {
            // On Windows this is the normal outcome when another copy already owns the pipe.
            Logger.Debug($"Not listening for commands from other copies: {ex.Message}");
            return null;
        }
    }

    private AppCommandChannel(string endpoint, Socket? listener, NamedPipeServerStream? firstPipe,
        Action<AppCommand> onCommand)
    {
        _endpoint = endpoint;
        _listener = listener;
        _ = listener != null
            ? Task.Run(() => AcceptSocketsAsync(listener, onCommand, _cts.Token))
            : Task.Run(() => AcceptPipesAsync(firstPipe!, onCommand, _cts.Token));
    }

    private static NamedPipeServerStream CreatePipe(string name, bool firstInstance) =>
        new(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
            | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None));

    private static Stream? Connect(string endpoint)
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", endpoint, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                pipe.Connect(ConnectTimeoutMs);
                return pipe;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                pipe.Dispose();
                return null;
            }
        }

        if (!File.Exists(endpoint)) return null;
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(endpoint));
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }
    }

    private static async Task AcceptSocketsAsync(Socket listener, Action<AppCommand> onCommand, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try { client = await listener.AcceptAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                if (ct.IsCancellationRequested || ex is not SocketException) return;
                continue;
            }

            await using var stream = new NetworkStream(client, ownsSocket: true);
            await ServeAsync(stream, onCommand, ct).ConfigureAwait(false);
        }
    }

    private async Task AcceptPipesAsync(NamedPipeServerStream pipe, Action<AppCommand> onCommand, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await using (pipe)
                {
                    await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    await ServeAsync(pipe, onCommand, ct).ConfigureAwait(false);
                }
                if (ct.IsCancellationRequested) return;
                pipe = CreatePipe(_endpoint, firstInstance: false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException
                                           or IOException or UnauthorizedAccessException)
            {
                if (ct.IsCancellationRequested || ex is not IOException) return;
                try { pipe = CreatePipe(_endpoint, firstInstance: false); }
                catch (Exception) { return; }
            }
        }
    }

    private static async Task ServeAsync(Stream stream, Action<AppCommand> onCommand, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(IoTimeoutMs);

            string? line = await ReadLineAsync(stream, timeout.Token).ConfigureAwait(false);
            AppCommand? command = null;
            bool understood = line != null && AppCommand.TryParseLine(line, out command);
            byte[] reply = Encoding.UTF8.GetBytes((understood ? Accepted : "unknown") + "\n");
            await stream.WriteAsync(reply, timeout.Token).ConfigureAwait(false);
            await stream.FlushAsync(timeout.Token).ConfigureAwait(false);

            if (understood) onCommand(command!);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException
                                       or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Logger.Warn("A command from another copy of the program failed.", ex);
        }
    }

    /// <summary>Reads one line of at most <see cref="MaxLineBytes"/>; null if none arrives.</summary>
    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxLineBytes];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length, 1), ct).ConfigureAwait(false);
            if (read == 0) return null;
            if (buffer[length] == (byte)'\n') return Encoding.UTF8.GetString(buffer, 0, length).Trim();
            length++;
        }
        return null;
    }

    public void Dispose()
    {
        if (_cts.IsCancellationRequested) return;
        _cts.Cancel();
        if (_listener != null)
        {
            _listener.Dispose();
            try { File.Delete(_endpoint); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private const int AnyProcess = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
