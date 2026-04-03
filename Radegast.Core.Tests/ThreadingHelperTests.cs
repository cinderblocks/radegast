/**
 * Radegast Metaverse Client
 * Copyright (c) 2026, Sjofn, LLC
 * All rights reserved.
 *
 * Radegast is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.If not, see<https://www.gnu.org/licenses/>.
 */

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Radegast.Tests
{
    /// <summary>
    /// Minimal ISynchronizeInvoke stub for ThreadingHelper tests.
    /// When InvokeRequired is false the helper calls the action directly on the
    /// calling thread, which is the only path we need to drive synchronously.
    /// When InvokeRequired is true, BeginInvoke/Invoke execute the delegate
    /// synchronously so the TCS in SafeInvokeAsync is already resolved by the
    /// time the method returns.
    /// </summary>
    internal class FakeSynchronizeInvoke : ISynchronizeInvoke
    {
        public bool InvokeRequired { get; set; }

        public IAsyncResult BeginInvoke(Delegate method, object[] args)
        {
            method.DynamicInvoke(args);
            return null;
        }

        public object EndInvoke(IAsyncResult result) => null;

        public object Invoke(Delegate method, object[] args) => method.DynamicInvoke(args);
    }

    [TestFixture]
    public class ThreadingHelperTests
    {
        #region SafeInvoke — null guards

        [Test]
        public void SafeInvoke_NullInvoker_DoesNotCallAction()
        {
            bool called = false;
            ThreadingHelper.SafeInvoke(null, () => called = true);
            Assert.That(called, Is.False);
        }

        [Test]
        public void SafeInvoke_NullAction_DoesNotThrow()
        {
            var invoker = new FakeSynchronizeInvoke { InvokeRequired = false };
            Assert.DoesNotThrow(() => ThreadingHelper.SafeInvoke(invoker, null));
        }

        #endregion

        #region SafeInvoke — direct call (InvokeRequired = false)

        [Test]
        public void SafeInvoke_InvokeNotRequired_CallsActionDirectly()
        {
            bool called = false;
            var invoker = new FakeSynchronizeInvoke { InvokeRequired = false };
            ThreadingHelper.SafeInvoke(invoker, () => called = true);
            Assert.That(called, Is.True);
        }

        [Test]
        public void SafeInvoke_InvokeNotRequired_ActionRunsOnCallingThread()
        {
            int actionThreadId = -1;
            var invoker = new FakeSynchronizeInvoke { InvokeRequired = false };
            ThreadingHelper.SafeInvoke(invoker, () => actionThreadId = Thread.CurrentThread.ManagedThreadId);
            Assert.That(actionThreadId, Is.EqualTo(Thread.CurrentThread.ManagedThreadId));
        }

        #endregion

        #region SafeInvokeSync — null guards

        [Test]
        public void SafeInvokeSync_NullInvoker_DoesNotCallAction()
        {
            bool called = false;
            ThreadingHelper.SafeInvokeSync(null, () => called = true);
            Assert.That(called, Is.False);
        }

        [Test]
        public void SafeInvokeSync_NullAction_DoesNotThrow()
        {
            var invoker = new FakeSynchronizeInvoke { InvokeRequired = false };
            Assert.DoesNotThrow(() => ThreadingHelper.SafeInvokeSync(invoker, null));
        }

        #endregion

        #region SafeInvokeSync — direct call (InvokeRequired = false)

        [Test]
        public void SafeInvokeSync_InvokeNotRequired_CallsActionDirectly()
        {
            bool called = false;
            var invoker = new FakeSynchronizeInvoke { InvokeRequired = false };
            ThreadingHelper.SafeInvokeSync(invoker, () => called = true);
            Assert.That(called, Is.True);
        }

        #endregion

        #region SafeInvokeAsync (Action) — null guards and cancellation

        [Test]
        public async Task SafeInvokeAsync_NullInvoker_ReturnsCompletedTask()
        {
            Task task = ThreadingHelper.SafeInvokeAsync((System.ComponentModel.ISynchronizeInvoke)null, () => { });
            await task;
            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        [Test]
        public async Task SafeInvokeAsync_NullAction_ReturnsCompletedTask()
        {
            var invoker = new FakeSynchronizeInvoke { InvokeRequired = false };
            Task task = ThreadingHelper.SafeInvokeAsync(invoker, (Action)null);
            await task;
            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        [Test]
        public void SafeInvokeAsync_AlreadyCancelledToken_ReturnsCancelledTask()
        {
            var invoker = new FakeSynchronizeInvoke { InvokeRequired = false };
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Task task = ThreadingHelper.SafeInvokeAsync(invoker, () => { }, cts.Token);
            Assert.That(task.IsCanceled, Is.True);
        }

        [Test]
        public async Task SafeInvokeAsync_InvokeNotRequired_CallsActionAndCompletesTask()
        {
            bool called = false;
            var invoker = new FakeSynchronizeInvoke { InvokeRequired = false };
            await ThreadingHelper.SafeInvokeAsync(invoker, () => called = true);
            Assert.That(called, Is.True);
        }

        #endregion

        #region SafeInvokeAsync (Func<Task>) — null guards and cancellation

        [Test]
        public async Task SafeInvokeAsync_FuncTask_NullInvoker_ReturnsCompletedTask()
        {
            Task task = ThreadingHelper.SafeInvokeAsync(null, () => Task.CompletedTask);
            await task;
            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        [Test]
        public void SafeInvokeAsync_FuncTask_AlreadyCancelledToken_ReturnsCancelledTask()
        {
            var invoker = new FakeSynchronizeInvoke { InvokeRequired = false };
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Task task = ThreadingHelper.SafeInvokeAsync(invoker, () => Task.CompletedTask, cts.Token);
            Assert.That(task.IsCanceled, Is.True);
        }

        #endregion
    }
}
