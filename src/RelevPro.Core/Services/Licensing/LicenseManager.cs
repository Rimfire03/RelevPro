using TomLine.Licensing;

namespace RelevPro.Core.Services.Licensing;

/// <summary>Façade de licence de RelevPro : conserve l'API et les textes existants, la logique (validation, grâce
/// hors-ligne, réponses signées, licence.ini) est portée par le paquet partagé TomLine.Licensing.</summary>
public static class LicenseManager
{
    internal const string ProductSlug = "relevpro";

    private static readonly LicenseOptions Options = new()
    {
        ProductSlug = ProductSlug,
        StoreName = "RelevPro",   // non utilisé : le stockage est celui de RelevPro (RelevLicenseStore)
        // Clé publique du serveur de licences (vérification seule).
        PublicKeys = new[] { "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEjairPbdPQuYiWSJ6MtVo6xchepZYn9h+JFdSI72MNzuVwmFgTCiYBRuPHJBJT45eC+qloOQbBWZDSZSkZxttvQ==" },
        RequireSignature = true,
        // Une licence expirée reste stockée (une prolongation côté serveur la rend valide) ; seules « révoquée » et
        // « introuvable » l'effacent. Un poste oublié par le serveur est réactivé automatiquement.
        KeepLicenseOnRecoverableRefusal = true,
        ReactivateWhenDeviceNotActivated = true
    };

    private static LicenseClient? _client;
    private static LicenseService? _svc;
    private static string? _deviceId;

    /// <summary>Service de licence partagé (informations client demandées par le serveur : ProfileRequest, SubmitProfileAsync).</summary>
    public static LicenseService Service => Svc();

    private static LicenseService Svc()
    {
        if (_svc is not null) return _svc;
        var device = new WindowsDeviceIdentity();
        var id = device.DeviceId;
        var client = new LicenseClient(Options);
        _svc = new LicenseService(client, device, new RelevLicenseStore(id), new RelevBypassFile());
        _client = client;
        _deviceId = id;
        return _svc;
    }

    private static LicenseMode _mode = LicenseMode.Blocked;
    private static StoredLicense? Cs { get; set; }
    private static string? _lockReason;

    // Une licence mise en grâce hors ligne (serveur injoignable) ne dépasse jamais sa date d'expiration
    // (démo / expiring) : le mode effectif bascule alors tout seul sur Expired, sans attendre une
    // revalidation (qui, hors ligne, ne pourrait de toute façon pas conclure autrement).
    public static LicenseMode Mode
    {
        get => _mode == LicenseMode.Grace && Cs is { } l && IsPast(l) ? LicenseMode.Expired : _mode;
        private set => _mode = value;
    }

    /// <summary>Déclenché après chaque changement d'état (activation, validation, expiration, démo...). Peut
    /// l'être depuis n'importe quel thread : les abonnés ramènent eux-mêmes sur le thread d'interface.</summary>
    public static event Action? Changed;

    private static void Ch() { try { Changed?.Invoke(); } catch { } }

    /// <summary>Interrupteur global de build : aucun contrôle de licence ni aucun appel réseau.</summary>
    public static bool IsGlobalFree => Zq1.K2;

    /// <summary>Nom du client lu dans licence.ini (ou reçu du serveur), pour l'affichage "Licence gratuite — nom".</summary>
    private static volatile string? BypassName;

    /// <summary>Déclenché (thread quelconque) après suppression de licence.ini sur ordre du serveur.</summary>
    public static event Action? BypassRemoved;

    private static DateTime _lastPing = DateTime.MinValue;
    private static int _pingBusy;

    /// <summary>Mode bypass (licence.ini présent) : ping best-effort au démarrage puis toutes les 24 h, en arrière-plan,
    /// timeout 5 s, toute erreur ignorée. Le nom du client est relu dans licence.ini avant chaque ping. Jamais de
    /// clé de licence dans l'appel. Ordre "remove_bypass" : suppression du fichier, accusé, puis événement
    /// <see cref="BypassRemoved"/> (l'application relance le flux normal de licence).</summary>
    public static async Task BypassPingAsync()
    {
        if (Zq1.K2 || !Nf8.F1()) return;
        if (System.Threading.Interlocked.Exchange(ref _pingBusy, 1) == 1) return;
        try
        {
            _lastPing = DateTime.UtcNow;
            var name = await Task.Run(Nf8.F2);
            if (name != BypassName) { BypassName = name; Ch(); }

            Svc();
            var reply = await _client!.BypassPingAsync(_deviceId!, Environment.MachineName, name);
            if (reply.AllCommands.Contains("remove_bypass", StringComparer.OrdinalIgnoreCase) && Nf8.Rm1())
            {
                try { await _client.BypassRemovedAsync(_deviceId!); } catch { }
                BypassName = null;
                try { BypassRemoved?.Invoke(); } catch { }
            }
        }
        catch { }
        finally { System.Threading.Interlocked.Exchange(ref _pingBusy, 0); }
    }

    public static bool IsFreeLicense => Mode == LicenseMode.Free;
    public static bool IsBlocked => Mode == LicenseMode.Blocked;
    public static bool IsExpired => Mode == LicenseMode.Expired;
    public static bool IsOfflineGrace => Mode == LicenseMode.Grace;

    /// <summary>Vrai pour une démo (type "demo") en cours, pour l'affichage uniquement : l'accès aux modules
    /// ne dépend jamais du type, seulement de license.features (voir <see cref="HasFeature"/>).</summary>
    public static bool IsDemo => string.Equals(Cs?.Type, "demo", StringComparison.OrdinalIgnoreCase);

    // Texte et alerte du pied de page : présentation commune des applications (LicenseFooter, TomLine.Licensing).
    private static FooterInfo Footer()
    {
        var state = Mode switch
        {
            LicenseMode.Free => new LicenseState(LicenseStatus.Bypass),
            LicenseMode.Active => new LicenseState(LicenseStatus.Valid, Cs),
            LicenseMode.Grace => new LicenseState(LicenseStatus.OfflineGrace, Cs),
            LicenseMode.Expired => new LicenseState(LicenseStatus.Locked, Cs, _lockReason ?? "license_expired"),
            _ => new LicenseState(LicenseStatus.NoLicense)
        };
        return LicenseFooter.Describe(state, DateTimeOffset.UtcNow, BypassName);
    }

    public static string FooterText => Footer().Text;

    /// <summary>Compteur de postes (Â« Postes : 2/5 Â»), ou null quand le serveur ne l'a pas renvoyÃ©.</summary>
    public static string? PostsText => Footer().PostsLine;

    /// <summary>Texte d'Ã©tat avec le compteur de postes quand il est connu (Â« Licence active  Â·  Postes : 2/5 Â»).</summary>
    public static string FooterTextWithPosts => Footer() is { PostsLine: { } p } f ? f.Text + "  Â·  " + p : FooterText;

    /// <summary>Statut à afficher en couleur d'alerte : licence expirée, ou démo à 2 jours de la fin ou moins.</summary>
    public static bool FooterIsAlert => Footer().IsAlert;

    /// <summary>Un module n'est disponible que s'il figure dans license.features (licence valide) : aucune
    /// règle spéciale selon le type (une démo reçoit simplement la liste complète du serveur).</summary>
    public static bool HasFeature(string f)
    {
        var m = Mode;
        if (m == LicenseMode.Free) return true;
        if (m != LicenseMode.Active && m != LicenseMode.Grace) return false;
        return Cs?.Features.Contains(f) ?? false;
    }

    public static async Task<LicenseCheckOutcome> CheckAtStartupAsync()
    {
        if (Zq1.K2) { Mode = LicenseMode.Free; return new LicenseCheckOutcome(LicenseMode.Free, FooterText); }
        if (Nf8.F1())
        {
            Mode = LicenseMode.Free;
            _ = BypassPingAsync(); // arrière-plan, ne retarde jamais le démarrage
            return new LicenseCheckOutcome(LicenseMode.Free, FooterText);
        }

        try
        {
            return FromState(await Svc().StartAsync());
        }
        catch (InvalidOperationException)
        {
            Cs = null;
            Mode = LicenseMode.Blocked;
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Identifiant du poste indisponible.");
        }
    }

    /// <summary>Revalidation de la licence stockée (périodique, ou bouton "Réessayer" d'une licence expirée).
    /// Sans effet en licence gratuite (aucun appel réseau).</summary>
    public static async Task<LicenseCheckOutcome> RevalidateAsync()
    {
        if (Mode == LicenseMode.Free)
        {
            if (DateTime.UtcNow - _lastPing >= TimeSpan.FromHours(24)) _ = BypassPingAsync();
            return new LicenseCheckOutcome(LicenseMode.Free, FooterText);
        }

        return FromState(await Svc().RevalidateAsync());
    }

    /// <summary>Active une clé saisie. Sert aussi à "changer de licence" : la nouvelle clé est activée
    /// d'abord, l'ancienne n'est désactivée (côté serveur) qu'ensuite ; en cas d'échec, l'ancienne reste
    /// stockée et en vigueur.</summary>
    public static async Task<LicenseCheckOutcome> ActivateAsync(string key)
    {
        key = key.Trim();
        if (key.Length == 0) return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Merci de saisir une clé de licence.");

        try
        {
            var svc = Svc();
            var previous = Nf8.L();
            var state = await svc.ActivateAsync(key);
            if (state.Status == LicenseStatus.Valid)
            {
                Cs = state.License;
                _lockReason = null;
                Mode = LicenseMode.Active;

                if (previous is not null && !string.IsNullOrWhiteSpace(previous.K) && previous.K != key)
                    await DeactivateRemoteAsync(previous.K, previous.D);

                Ch();
                // Compteur de postes connu : Â« Licence activÃ©e (poste 2/5) Â» (rien de plus avec un ancien serveur).
                return new LicenseCheckOutcome(LicenseMode.Active, FooterText,
                    InfoMessage: state.License?.ActivationsText is not null ? LicenseFooter.ActivationMessage(state) : null);
            }
            if (state.Reason == "network")
                return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Impossible de contacter le serveur de licences. Vérifiez votre connexion internet et réessayez.");
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText,
                state.Reason == "activation_limit_reached" ? LicenseReasons.DescribeRefusal(state) : Rm(state.Reason));
        }
        catch
        {
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Impossible de contacter le serveur de licences. Vérifiez votre connexion internet et réessayez.");
        }
    }

    /// <summary>Démo de 7 jours demandée au serveur, sans clé à saisir : le serveur crée et active la démo
    /// pour ce poste, la clé reçue est stockée directement (jamais affichée, copiée ni journalisée) et
    /// n'a pas à être activée ensuite. Exige une connexion (pas de grâce sans validation préalable).</summary>
    public static async Task<LicenseCheckOutcome> RequestDemoAsync(DemoRequestProfile profile)
    {
        if (Mode == LicenseMode.Free) return new LicenseCheckOutcome(LicenseMode.Free, FooterText);

        try
        {
            var state = await Svc().RequestDemoAsync(profile);
            if (state.Status == LicenseStatus.Valid && state.License is { } lic)
            {
                Cs = lic;
                _lockReason = null;
                Mode = LicenseMode.Active;
                Ch();
                var info = lic.ExpiresAt is { } e
                    ? $"Démo activée : tous les modules sont disponibles jusqu'au {Lc(e.UtcDateTime):dd/MM/yyyy}."
                    : "Démo activée : tous les modules sont disponibles.";
                return new LicenseCheckOutcome(LicenseMode.Active, FooterText, InfoMessage: info);
            }
            if (state.Reason == "network")
                return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Impossible de joindre le serveur de licences, vérifiez votre connexion.");
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, Dm(state.Reason));
        }
        catch
        {
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Impossible de joindre le serveur de licences, vérifiez votre connexion.");
        }
    }

    public static async Task DeactivateAsync()
    {
        Svc();
        var s = Nf8.L();
        if (s is not null) await DeactivateRemoteAsync(s.K, _deviceId!);
        Nf8.X();   // la licence locale est retirée même si le serveur est injoignable
        Cs = null;
        Mode = LicenseMode.Blocked;
        Ch();
    }

    private static async Task DeactivateRemoteAsync(string key, string deviceId)
    {
        try { await _client!.DeactivateAsync(key, string.IsNullOrWhiteSpace(deviceId) ? _deviceId! : deviceId); }
        catch { }
    }

    // Traduit l'état du service partagé en mode d'affichage de RelevPro. Une licence expirée est conservée en
    // mode Expired (une prolongation côté serveur la rend de nouveau valide : voir RevalidateAsync) ; le serveur
    // injoignable ouvre la grâce hors ligne (30 jours après la dernière validation, plafonnée à l'échéance).
    private static LicenseCheckOutcome FromState(LicenseState st)
    {
        _lockReason = null;
        switch (st.Status)
        {
            case LicenseStatus.Valid:
                Cs = st.License;
                Mode = LicenseMode.Active;
                Ch();
                return new LicenseCheckOutcome(LicenseMode.Active, FooterText);

            case LicenseStatus.OfflineGrace:
                Cs = st.License;
                Mode = LicenseMode.Grace;
                Ch();
                if (Mode == LicenseMode.Expired)
                    return new LicenseCheckOutcome(LicenseMode.Expired, FooterText, "Licence expirée. Impossible de joindre le serveur de licences, vérifiez votre connexion.");
                return new LicenseCheckOutcome(LicenseMode.Grace, FooterText, IsOfflineGrace: true);

            case LicenseStatus.Bypass:
                BypassName = Nf8.F2();
                Mode = LicenseMode.Free;
                Ch();
                return new LicenseCheckOutcome(LicenseMode.Free, FooterText);

            case LicenseStatus.Locked when st.License is not null
                                           && st.Reason is "license_expired" or "offline_grace_expired" or "clock_tampered":
                _lockReason = st.Reason;
                Cs = st.License;
                Mode = LicenseMode.Expired;
                Ch();
                return new LicenseCheckOutcome(LicenseMode.Expired, FooterText,
                    st.Reason == "license_expired" ? Rm(st.Reason) : LicenseReasons.Describe(st.Reason));

            case LicenseStatus.Locked:
                Cs = null;
                Mode = LicenseMode.Blocked;
                Ch();
                return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, Rm(st.Reason));

            default:
                Cs = null;
                Mode = LicenseMode.Blocked;
                Ch();
                return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Aucune licence active. Saisissez votre clé de licence pour continuer.");
        }
    }

    // Date d'expiration dépassée (démo / expiring ; une licence perpétuelle n'en a pas).
    private static bool IsPast(StoredLicense l) => l.ExpiresAt is { } e && e.UtcDateTime <= DateTime.UtcNow;

    private static DateTime Lc(DateTime e) => e.Kind == DateTimeKind.Utc ? e.ToLocalTime() : e;

    private static string Rm(string? r) => r switch
    {
        "license_not_found" => "Clé de licence introuvable.",
        "license_revoked" => "Cette licence a été révoquée.",
        "license_expired" => "Licence expirée.",
        "activation_limit_reached" => "Le nombre maximal d'activations pour cette licence est atteint.",
        "device_not_activated" => "Cet appareil n'a pas été activé pour cette licence.",
        _ => "La licence n'est pas valide."
    };

    private static string Dm(string? r) => r switch
    {
        "license_expired" => "La démo de ce poste est terminée. Saisissez une clé de licence.",
        "license_revoked" => "Cette démo a été désactivée. Saisissez une clé de licence.",
        "product_not_found" => "Produit introuvable sur le serveur de licences.",
        "profile_invalid" => "Le formulaire de demande est incomplet ou invalide : vérifiez vos informations.",
        _ => "La démo n'a pas pu être activée."
    };
}
