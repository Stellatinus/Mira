namespace Mira.Server.Storage;

/// <summary>
/// Dove vengono conservati i token. Un'unica interfaccia: oggi c'è l'implementazione su file
/// cifrato (identica su Windows, Linux e macOS), domani si può aggiungere il portachiavi di sistema.
/// </summary>
public interface ISecretStore
{
    Task<IReadOnlyDictionary<string, string>> LoadAllAsync(CancellationToken ct = default);
    Task SaveAsync(string name, string secret, CancellationToken ct = default);
    Task RemoveAsync(string name, CancellationToken ct = default);
}
