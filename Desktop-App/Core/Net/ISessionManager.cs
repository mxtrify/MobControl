namespace MobControlUI.Core.Net
{
    public interface ISessionManager
    {
        int Port { get; set; }
        string HostAddress { get; }   // initial OS guess

        string CreateSession();       // returns token
        bool Validate(string token);
        void CloseSession(string token);

        // Use the server’s BoundPrefixes/ActualPort to build a correct URL for the client
        string BuildUrl(string token, TokenWebSocketServer server); // ws://{chosenHost}:{server.ActualPort}/ws?token={token}
    }
}
