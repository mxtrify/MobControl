using System.Collections.Generic;
using System.Threading.Tasks;
using MobControlUI.Core.Mapping;

namespace MobControlUI.Core.Storage
{
    public interface IInputMappingStore
    {
        Task<IReadOnlyList<string>> ListAsync();
        Task<bool> ExistsAsync(string name);

        Task SaveAsync<T>(string name, T mapping);
        Task SaveAsync(string name, InputMappingFile file);

        Task<T?> LoadAsync<T>(string name);

        // Tolerant loader (supports both legacy and new schemas)
        Task<InputMappingFile?> LoadFlexibleAsync(string name);

        Task DeleteAsync(string name);
        Task RenameAsync(string oldName, string newName);

        Task SeedDefaultsAsync(bool overwrite = false);
    }
}