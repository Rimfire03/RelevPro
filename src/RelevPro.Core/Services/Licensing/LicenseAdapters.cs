using TomLine.Licensing;

namespace RelevPro.Core.Services.Licensing;

/// <summary>Stockage de la licence de RelevPro (format historique <see cref="Tu5"/>, chiffré par <see cref="Nf8"/>,
/// registre HKLM en version installée, fichier sinon) exposé au système de licence partagé.</summary>
internal sealed class RelevLicenseStore : ILicenseStore
{
    private readonly string _deviceId;

    public RelevLicenseStore(string deviceId) => _deviceId = deviceId;

    public StoredLicense? Load()
    {
        var s = Nf8.L();
        if (s is null || string.IsNullOrWhiteSpace(s.K)) return null;
        return new StoredLicense
        {
            Key = s.K,
            Type = s.L?.T,
            CustomerName = s.L?.Cn,
            ExpiresAt = s.L?.Ex is { } e ? new DateTimeOffset(e.ToUniversalTime()) : null,
            Features = s.L?.Ft is { } f ? new List<string>(f) : new List<string>(),
            MaxActivations = s.L?.Mx,
            LastValidatedAt = s.V == default ? null : new DateTimeOffset(DateTime.SpecifyKind(s.V, DateTimeKind.Utc)),
            LastSeenAt = s.S is { } seen ? new DateTimeOffset(DateTime.SpecifyKind(seen, DateTimeKind.Utc)) : null
        };
    }

    public void Save(StoredLicense l) => Nf8.S(new Tu5
    {
        D = _deviceId,
        K = l.Key,
        V = l.LastValidatedAt?.UtcDateTime ?? DateTime.UtcNow,
        S = l.LastSeenAt?.UtcDateTime,
        L = new Vw2
        {
            K = l.Key,
            P = LicenseManager.ProductSlug,
            T = l.Type,
            Ex = l.ExpiresAt?.UtcDateTime,
            Mx = l.MaxActivations ?? 0,
            Ft = l.Features,
            Cn = l.CustomerName
        }
    });

    public void Clear() => Nf8.X();
}

/// <summary>licence.ini de RelevPro (emplacement et format historiques, voir <see cref="Nf8"/>) exposé au système de
/// licence partagé : le comportement du fichier ne change pas.</summary>
internal sealed class RelevBypassFile : IBypassFile
{
    public bool Exists => Nf8.F1();
    public string? ReadCustomerName() => Nf8.F2();
    public bool TryCreate(string? content) => Nf8.W(content);
    public bool TryDelete() => Nf8.Rm1();
}
