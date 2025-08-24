using System.Collections.Generic;

namespace MobControlUI.Core.Mapping
{
    // Unified on-disk schema for a mapping file
    public sealed class InputMappingFile
    {
        public int Version { get; set; } = 1;
        public string Layout { get; set; } = "";
        public Dictionary<string, string> Bindings { get; set; } = new();
    }
}