using System;
using System.Collections.Generic;
using System.Linq;

namespace MobControlUI.Core.Players
{
    public interface IPlayerIdAllocator
    {
        void SetCapacity(int maxIds);                 // usually = number of connected devices
        IReadOnlyList<int> Pool();                    // 1..capacity (read-only snapshot)

        bool TryReserve(int id, object owner);        // owner = your ControllerViewModel instance
        void Release(int id, object owner);
        void ReleaseAll(object owner);

        bool IsOwnedBy(int id, object owner);
        bool IsTaken(int id);

        event Action? Changed;                        // fire on any reservation/capacity change
    }

    /// <summary>
    /// In-memory, thread-safe allocator that enforces unique Player IDs.
    /// Owners are held via WeakReference so rows GC cleanly.
    /// </summary>
    public sealed class PlayerIdAllocator : IPlayerIdAllocator
    {
        private readonly object _sync = new();
        private int _capacity = 0;

        // id -> owner (weak)
        private readonly Dictionary<int, WeakReference<object>> _owners = new();

        public event Action? Changed;

        public void SetCapacity(int maxIds)
        {
            if (maxIds < 0) maxIds = 0;

            bool changed = false;
            lock (_sync)
            {
                if (_capacity != maxIds)
                {
                    _capacity = maxIds;
                    // drop ids out of range or with dead owners
                    var toRemove = _owners
                        .Where(kv => kv.Key < 1 || kv.Key > _capacity || !kv.Value.TryGetTarget(out _))
                        .Select(kv => kv.Key)
                        .ToList();
                    foreach (var id in toRemove) _owners.Remove(id);
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }

        public IReadOnlyList<int> Pool()
        {
            lock (_sync)
            {
                if (_capacity <= 0) return Array.Empty<int>();
                return Enumerable.Range(1, _capacity).ToArray();
            }
        }

        public bool TryReserve(int id, object owner)
        {
            if (owner is null) throw new ArgumentNullException(nameof(owner));

            bool ok;
            lock (_sync)
            {
                if (id < 1 || id > _capacity) return false;

                CleanupDead_NoLock();

                if (_owners.TryGetValue(id, out var wr) && wr.TryGetTarget(out var existing))
                {
                    ok = ReferenceEquals(existing, owner); // already mine -> ok
                }
                else
                {
                    _owners[id] = new WeakReference<object>(owner);
                    ok = true;
                }
            }
            if (ok) Changed?.Invoke();
            return ok;
        }

        public void Release(int id, object owner)
        {
            if (owner is null) return;

            bool changed = false;
            lock (_sync)
            {
                if (_owners.TryGetValue(id, out var wr) && wr.TryGetTarget(out var existing) &&
                    ReferenceEquals(existing, owner))
                {
                    _owners.Remove(id);
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }

        public void ReleaseAll(object owner)
        {
            if (owner is null) return;

            bool changed = false;
            lock (_sync)
            {
                CleanupDead_NoLock();

                var mine = _owners
                    .Where(kv => kv.Value.TryGetTarget(out var x) && ReferenceEquals(x, owner))
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var id in mine) _owners.Remove(id);
                changed = mine.Count > 0;
            }
            if (changed) Changed?.Invoke();
        }

        public bool IsOwnedBy(int id, object owner)
        {
            if (owner is null) return false;

            lock (_sync)
            {
                return _owners.TryGetValue(id, out var wr) &&
                       wr.TryGetTarget(out var existing) &&
                       ReferenceEquals(existing, owner);
            }
        }

        public bool IsTaken(int id)
        {
            lock (_sync)
            {
                return _owners.TryGetValue(id, out var wr) && wr.TryGetTarget(out _);
            }
        }

        private void CleanupDead_NoLock()
        {
            var dead = _owners.Where(kv => !kv.Value.TryGetTarget(out _))
                              .Select(kv => kv.Key)
                              .ToList();
            foreach (var id in dead) _owners.Remove(id);
        }
    }
}