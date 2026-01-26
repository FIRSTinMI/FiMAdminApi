using Firebase.Database;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.DependencyInjection;

namespace FiMAdminApi.Data.Firebase;

public static class FirebaseServiceCollectionExtensions
{
    public static async Task<IServiceCollection> AddFirebaseFromAppDefaultCredentials(this IServiceCollection services,
        string? baseUrl)
    {
        // Note: FrcFirebaseRepository should gracefully handle not having a Firebase client. Callers don't care if
        // Firebase is set up or not, they just throw data at the FrcFirebaseRepository and let it deal with whether
        // it should do anything with it.
        services.AddScoped<FrcFirebaseRepository>();

        if (string.IsNullOrWhiteSpace(baseUrl)) return services;
        
        var accountCred = await GoogleCredential.GetApplicationDefaultAsync();
        var credential = accountCred.CreateScoped(
            "https://www.googleapis.com/auth/userinfo.email",
            "https://www.googleapis.com/auth/firebase.database");

        services.AddSingleton(_ => new FirebaseClient(baseUrl,
            new FirebaseOptions
            {
                AuthTokenAsyncFactory = GetAccessToken,
                AsAccessToken = true
            }));

        return services;

        async Task<string> GetAccessToken()
        {
            return await (credential as ITokenAccess).GetAccessTokenForRequestAsync();
        }
    }
}