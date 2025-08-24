using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MobControlUI.Core.Mapping
{
    public interface IActiveMappingService
    {
        void Set(Guid deviceId, string mappingName);
        string? Get(Guid deviceId);
        void Clear(Guid deviceId);
    }

    public sealed class ActiveMappingService : IActiveMappingService
    {
        private readonly Dictionary<Guid, string> _active = new();
        private readonly object _gate = new();
        public void Set(Guid id, string name) { lock (_gate) _active[id] = name; }
        public string? Get(Guid id) { lock (_gate) return _active.TryGetValue(id, out var m) ? m : null; }
        public void Clear(Guid id) { lock (_gate) _active.Remove(id); }
    }
}
