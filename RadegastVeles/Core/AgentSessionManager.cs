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
using System.Collections.Generic;
using System.Linq;

namespace Radegast.Veles.Core;

public sealed class AgentSessionManager : IDisposable
{
    private readonly List<AgentSession> _sessions = [];

    public IReadOnlyList<AgentSession> Sessions => _sessions;

    public event EventHandler<AgentSession>? SessionAdded;
    public event EventHandler<AgentSession>? SessionRemoved;

    public AgentSession AddSession(RadegastInstanceAvalonia instance)
    {
        var session = new AgentSession(instance);
        _sessions.Add(session);
        SessionAdded?.Invoke(this, session);
        return session;
    }

    public void RemoveSession(AgentSession session)
    {
        if (!_sessions.Remove(session)) return;
        SessionRemoved?.Invoke(this, session);
        session.Dispose();
    }

    public AgentSession? FindByInstance(RadegastInstanceAvalonia instance)
        => _sessions.FirstOrDefault(s => s.Instance == instance);

    public AgentSession? FindById(Guid id)
        => _sessions.FirstOrDefault(s => s.Id == id);

    public void Dispose()
    {
        foreach (var session in _sessions.ToArray())
        {
            RemoveSession(session);
        }
    }
}
