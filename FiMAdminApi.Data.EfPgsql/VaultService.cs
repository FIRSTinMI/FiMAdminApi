using Microsoft.EntityFrameworkCore;

namespace FiMAdminApi.Data.EfPgsql;

public class VaultService(DataContext dbContext)
{
    public async Task<string?> GetSecret(string name)
    {
        return (await InternalGetSecret(name))?.DecryptedSecret;
    }

    public async Task<Dictionary<string, string>> GetSecretsByPrefix(string prefix)
    {
        var pattern = prefix + "%";
        var secrets = await dbContext.Database
            .SqlQuery<Secret>($"select id, name, decrypted_secret from vault.decrypted_secrets where name like {pattern}")
            .ToListAsync();

        return secrets.ToDictionary(s => s.Name, s => s.DecryptedSecret ?? string.Empty);
    }

    public async Task UpsertSecret(string name, string value)
    {
        var existingSecret = await InternalGetSecret(name);

        if (existingSecret is null)
        {
            await dbContext.Database.ExecuteSqlAsync($"select vault.create_secret({value}, {name});");
        }
        else
        {
            await dbContext.Database.ExecuteSqlAsync(
                $"select vault.update_secret({existingSecret.Id}, {value}, {name});");
        }
    }

    private async Task<Secret?> InternalGetSecret(string name)
    {
        var secret = await dbContext.Database
            .SqlQuery<Secret>($"select id, name, decrypted_secret from vault.decrypted_secrets where name = {name}")
            .SingleOrDefaultAsync();

        return secret;
    }
    
    private record Secret(Guid Id, string Name, string DecryptedSecret);
}