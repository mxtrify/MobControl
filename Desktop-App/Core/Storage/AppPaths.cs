using System;
using System.IO;

namespace MobControlUI.Core.Storage
{
    public static class AppPaths
    {
        // Roaming (follows the user). Use LocalApplicationData if you prefer machine-local.
        private static readonly string BaseDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MobControlUI");

        /// <summary>Where user-editable input mapping JSONs live at runtime.</summary>
        public static string MappingsDir => Path.Combine(BaseDir, "InputMappings");

        /// <summary>Where default JSONs are copied FROM on first run (bundled with the app).</summary>
        public static string DefaultsDir => Path.Combine(AppContext.BaseDirectory, "Storage", "Defaults");

        public static void EnsureCreated() => Directory.CreateDirectory(MappingsDir);

        public static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}