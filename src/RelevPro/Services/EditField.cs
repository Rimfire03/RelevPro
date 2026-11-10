namespace RelevPro.Services;

public enum EditFieldType
{
    Text,
    MultilineText,
    Int,
    Decimal,
    Date,
    Months,
    Combo,
    Checklist
}

public class EditField
{
    public required string Label { get; init; }
    public required EditFieldType Type { get; init; }
    public required Func<object?> GetValue { get; init; }
    public required Action<object?> SetValue { get; init; }
    public bool Required { get; init; }

    /// <summary>Libellés affichés pour <see cref="EditFieldType.Combo"/> uniquement. GetValue/SetValue
    /// portent l'index sélectionné dans cette liste.</summary>
    public List<string>? ComboItems { get; init; }

    /// <summary>Rend ce champ accessible uniquement selon la valeur courante (en direct, avant validation)
    /// d'un autre champ du même formulaire (ex. masquer "Référence roulement volute" tant que "Type de
    /// centrale" vaut "Entraînement direct") : voir <see cref="DynamicEditWindow"/>, qui réévalue ce champ
    /// à chaque changement de <see cref="EnabledWhenFieldEquals"/>. Désactivé : le champ est vidé (donc
    /// enregistré comme vide/null) et son contrôle grisé.</summary>
    public EditField? EnabledWhenFieldEquals { get; set; }
    public Func<object?, bool>? EnabledPredicate { get; set; }

    /// <summary>Texte toujours en majuscules (colonnes "Type", "Type de courroies", "Réf. roulement") :
    /// converti pendant la frappe par <see cref="DynamicEditWindow"/> et à l'enregistrement. Même conversion
    /// que la migration 24 des valeurs existantes (DatabaseMigrations).</summary>
    public bool Uppercase { get; init; }

    private static string? ToCase(string? value, bool uppercase) => uppercase ? value?.ToUpperInvariant() : value;

    public static EditField Text(string label, Func<string> get, Action<string> set, bool required = false, bool uppercase = false) => new()
    {
        Label = label,
        Type = EditFieldType.Text,
        GetValue = () => get(),
        SetValue = v => set(ToCase((string?)v, uppercase) ?? string.Empty),
        Required = required,
        Uppercase = uppercase
    };

    public static EditField NullableText(string label, Func<string?> get, Action<string?> set, bool uppercase = false) => new()
    {
        Label = label,
        Type = EditFieldType.Text,
        GetValue = () => get(),
        SetValue = v => set(string.IsNullOrWhiteSpace((string?)v) ? null : ToCase((string)v!, uppercase)),
        Uppercase = uppercase
    };

    public static EditField Multiline(string label, Func<string?> get, Action<string?> set) => new()
    {
        Label = label,
        Type = EditFieldType.MultilineText,
        GetValue = () => get(),
        SetValue = v => set(string.IsNullOrWhiteSpace((string?)v) ? null : (string)v!)
    };

    /// <summary>Comme <see cref="Multiline(string,Func{string},Action{string},bool)"/> mais pour un champ
    /// texte non-nullable (ex. "Nom de la centrale d'air"), avec validation "requis" comme <see cref="Text"/>.</summary>
    public static EditField Multiline(string label, Func<string> get, Action<string> set, bool required) => new()
    {
        Label = label,
        Type = EditFieldType.MultilineText,
        GetValue = () => get(),
        SetValue = v => set((string?)v ?? string.Empty),
        Required = required
    };

    public static EditField IntField(string label, Func<int> get, Action<int> set, bool required = false) => new()
    {
        Label = label,
        Type = EditFieldType.Int,
        GetValue = () => get(),
        SetValue = v => set(v is int i ? i : (int.TryParse(v?.ToString(), out var parsed) ? parsed : 0)),
        Required = required
    };

    public static EditField NullableInt(string label, Func<int?> get, Action<int?> set) => new()
    {
        Label = label,
        Type = EditFieldType.Int,
        GetValue = () => get(),
        SetValue = v => set(string.IsNullOrWhiteSpace(v?.ToString()) ? null : int.TryParse(v!.ToString(), out var parsed) ? parsed : null)
    };

    public static EditField DecimalField(string label, Func<decimal> get, Action<decimal> set) => new()
    {
        Label = label,
        Type = EditFieldType.Decimal,
        GetValue = () => get(),
        SetValue = v => set(decimal.TryParse(v?.ToString(), out var parsed) ? parsed : 0m)
    };

    public static EditField DateField(string label, Func<DateOnly?> get, Action<DateOnly?> set) => new()
    {
        Label = label,
        Type = EditFieldType.Date,
        GetValue = () => get()?.ToDateTime(TimeOnly.MinValue),
        SetValue = v => set(v is DateTime dt ? DateOnly.FromDateTime(dt) : null)
    };

    /// <summary>Nombre de filtres déjà à changer chaque mois (index 0 = janvier) : sert à colorer les mois du champ
    /// <see cref="EditFieldType.Months"/> (rouge : mois chargés, vert : mois peu chargés).</summary>
    public double[]? MonthLoad { get; init; }

    public static EditField MonthsField(string label, Func<List<int>> get, Action<List<int>> set, double[]? monthLoad = null) => new()
    {
        Label = label,
        MonthLoad = monthLoad,
        Type = EditFieldType.Months,
        GetValue = () => get(),
        SetValue = v => set((List<int>)v!)
    };

    /// <summary>Liste déroulante générique (ex. sélection d'une famille K7). <paramref name="items"/> est
    /// la liste des libellés affichés ; get/set portent l'index sélectionné dans cette liste.</summary>
    public static EditField ComboField(string label, List<string> items, Func<int> get, Action<int> set) => new()
    {
        Label = label,
        Type = EditFieldType.Combo,
        ComboItems = items,
        GetValue = () => get(),
        SetValue = v => set(v is int i ? i : 0)
    };

    /// <summary>Cases à cocher génériques (ex. "quels roulements ont été changés") : <paramref name="items"/>
    /// est la liste des libellés affichés (dans <see cref="ComboItems"/>), get/set portent les index cochés.</summary>
    public static EditField ChecklistField(string label, List<string> items, Func<List<int>> get, Action<List<int>> set) => new()
    {
        Label = label,
        Type = EditFieldType.Checklist,
        ComboItems = items,
        GetValue = () => get(),
        SetValue = v => set((List<int>)v!)
    };
}
