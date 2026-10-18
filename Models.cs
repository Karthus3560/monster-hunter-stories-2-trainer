using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace RidersToolkit;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; Notify(name); return true; }
}
public sealed class Command(Action<object?> action, Func<object?, bool>? can = null) : ICommand
{
    public bool CanExecute(object? parameter) => can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => action(parameter);
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
}
public enum NumberKind { Int32, UInt16, Byte, Float32 }
public static class Numbers
{
    public static int Width(NumberKind kind) => kind switch { NumberKind.UInt16 => 2, NumberKind.Byte => 1, _ => 4 };
    public static byte[] Encode(double value, NumberKind kind)
    {
        if (!double.IsFinite(value)) throw new ArgumentException("Enter a finite number.");
        if (kind != NumberKind.Float32 && value != Math.Truncate(value)) throw new ArgumentException("Use a whole number for this value.");
        return kind switch
        {
            NumberKind.Int32 when value >= 0 && value <= int.MaxValue => BitConverter.GetBytes((int)value),
            NumberKind.UInt16 when value >= 0 && value <= ushort.MaxValue => BitConverter.GetBytes((ushort)value),
            NumberKind.Byte when value >= 0 && value <= byte.MaxValue => [(byte)value],
            NumberKind.Float32 when value >= 0 && value <= float.MaxValue => BitConverter.GetBytes((float)value),
            _ => throw new ArgumentException("This value is outside the selected number format.")
        };
    }
    public static double Decode(ReadOnlySpan<byte> bytes, NumberKind kind) => kind switch
    {
        NumberKind.Int32 => BitConverter.ToInt32(bytes), NumberKind.UInt16 => BitConverter.ToUInt16(bytes),
        NumberKind.Byte => bytes[0], _ => BitConverter.ToSingle(bytes)
    };
    public static double Parse(string text) => double.TryParse(text.Trim().Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : throw new ArgumentException("Enter a valid number.");
}

public sealed record FeatureOption(string Value,string Label) { public override string ToString()=>Label; }
public sealed class Feature : Observable
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required string Glyph { get; init; }
    public required double Maximum { get; init; }
    public double Minimum { get; init; }
    public NumberKind Kind { get; set; } = NumberKind.Int32;
    private string _target = "100", _current = "—", _hotkey = "None";
    private bool _active, _bound, _favorite,_available;
    private string _waiting="waiting for game";
    public string Target { get => _target; set {if(Set(ref _target, value)){Notify(nameof(Multiplier));Notify(nameof(MultiplierLabel));Notify(nameof(ChoiceLabel));}} }
    public bool IsSlider=>Id is "damageTaken" or "damage" or "battleExp" or "walkSpeed" or "mountSpeed" or "swimSpeed" or "flySpeed" or "eggSpeed";
    public bool IsChoice=>Id=="prayerBuff";
    public FeatureOption[] ChoiceOptions=>[new("1","Training · EXP"),new("2","Gathering"),new("3","Finding · egg quality"),new("4","Lucky · battle rewards"),new("5","Kinship"),new("6","Adamant · defense"),new("7","Might · attack"),new("8","Recovery")];
    public string ChoiceLabel=>ChoiceOptions.FirstOrDefault(x=>x.Value==Target)?.Label??"Choose an effect";
    public double SliderStep=>Id=="damageTaken"?.25:Category=="Movement"?.25:Id=="battleExp"?1:.5;
    public double Multiplier {get=>double.TryParse(Target,NumberStyles.Float,CultureInfo.InvariantCulture,out double v)&&double.IsFinite(v)?Math.Clamp(v,Minimum,Maximum):Minimum;set=>Target=Math.Clamp(value,Minimum,Maximum).ToString("0.##",CultureInfo.InvariantCulture);}
    public string MultiplierLabel=>Multiplier.ToString("0.##",CultureInfo.InvariantCulture)+"×";
    public string Current { get => _current; set {if(Set(ref _current, value))Notify(nameof(Status));} }
    public string Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }
    public bool Active { get => _active; set { if (Set(ref _active, value)) Notify(nameof(Status)); } }
    public bool Bound { get => _bound; set { if (Set(ref _bound, value)) Notify(nameof(Status)); } }
    public bool Favorite { get => _favorite; set => Set(ref _favorite, value); }
    public bool Available { get=>_available;set=>Set(ref _available,value); }
    public string Waiting {get=>_waiting;set {if(Set(ref _waiting,value)) Notify(nameof(Status));}}
    public bool IsBulk=>Id is "items" or "support" or "materials" or "facility" or "growth";
    public bool IsNumeric=>Category!="Exploration"&&!IsSlider&&!IsChoice&&Id is not ("kinshipLevel" or "battleSpeed" or "noConsume" or "fieldItems" or "loot");
    public bool IsLoot=>Id=="loot";
    public string[] LootOptions=>["2","5","10"];
    public string Status => Active ? Bound?IsNumeric||Id=="retreat"?"Enabled":"Enabled · "+Current:"Armed · "+Waiting : Bound ? "Ready · automatic" : Waiting;
    public long Address { get; set; }
    public long RegionBase { get; set; }
    public long AllocationBase { get; set; }
    public double? LastObserved { get; set; }
    public double LastWritten { get; set; }
    public long? ExperienceBaseline { get; set; }
    public bool IsExperience => Id is "riderXp" or "monstieXp";
    public double CheckedTarget()
    { var value = Numbers.Parse(Target); if (value < Minimum || value > Maximum) throw new ArgumentException($"{Title}: choose a value from {Minimum:N0} to {Maximum:N0}."); if(IsChoice&&!ChoiceOptions.Any(c=>c.Value==Target))throw new ArgumentException("Choose a listed Prayer Pot effect."); if(IsLoot&&value is not (2 or 5 or 10))throw new ArgumentException("Choose 2×, 5× or 10× loot.");Numbers.Encode(value, Kind); return value; }
}

public static class Catalog
{
    public static ObservableCollection<Feature> Create() => new([
        F("riderHp", "Rider health", "Automatically maintain your Rider's battle HP.", "Battle", "\uE95E", 9999, "9999", "F1",NumberKind.UInt16),
        F("monstieHp", "Monstie health", "Automatically maintain your active Monstie's HP.", "Battle", "\uE8D7", 9999, "9999", "F2",NumberKind.UInt16),
        F("kinship", "Kinship gauge", "Maintain normal and mounted Kinship for you and your battle buddy.", "Battle", "\uE945", 100, "100", "F3",NumberKind.Byte),
        F("hearts", "Party hearts", "Automatically replenish your remaining hearts.", "Battle", "\uEB52", 3, "3", "F4",NumberKind.Byte),
        F("damage", "Damage boost", "Multiply allied Rider and Monstie attack. Final damage also depends on defense and skills. F5 toggles.", "Battle", "\uE747", 100, "2", "F5",NumberKind.Float32),
        F("turns", "Battle turns", "Maintain the battle turn counter at your chosen number.", "Battle", "\uE787", 999, "1", "None"),
        F("battleExp", "Battle EXP multiplier", "Multiply defeated enemies' EXP for your Rider and party. Enable before defeating them. Quest and fixed event rewards stay original.", "Battle", "\uE734", 20, "2", "None",NumberKind.Float32),
        F("kinshipLevel", "Maximum Kinship attack", "Maintain Level 3 while riding in battle, for you and your buddy. Separate from the Kinship gauge.", "Battle", "\uE945", 2, "2", "None",NumberKind.Byte),
        F("battleSpeed", "Battle fast-forward", "Run battles at 2× the current game pace. Normal pace returns after battle or when switched off. Ctrl+F10 toggles.", "Battle", "\uE768", 1, "1", "Ctrl+F10",NumberKind.Byte),
        F("damageTaken", "Damage taken", "Use native Prayer Pot protection for incoming damage. 1× is normal; 0× requests 100% protection. Applies from the next battle.", "Battle", "\uE73E", 1, "0.5", "None", NumberKind.Float32),
        F("walkSpeed", "Walking & running", "Speed up travel on foot. Switch off or reset to return to normal.", "Movement", "\uE726", 3, "1.5", "None",NumberKind.Float32),
        F("mountSpeed", "Mounted speed", "Speed up ground riding and the Dash riding action. Camera and battle speed stay original.", "Movement", "\uE7F4", 3, "1.5", "None",NumberKind.Float32),
        F("flySpeed", "Flying speed", "Increase forward gliding speed while flying. Flight must already be unlocked; take off to apply.", "Movement", "\uE709", 3, "1.5", "None",NumberKind.Float32),
        F("swimSpeed", "Swimming speed", "Speed up water riding. Swimming abilities must already be unlocked.", "Movement", "\uE774", 3, "1.5", "None",NumberKind.Float32),
        F("eggSpeed", "Egg carrying speed", "Carry eggs faster with a separate 1–3× speed setting. Switch off or Reset to restore normal carrying speeds.", "Movement", "\uE8D7", 3, "2", "None",NumberKind.Float32),
        F("money", "Zenny", "Set or maintain your spending money.", "Inventory", "\uE8D4", 9999999, "999999", "F6"),
        F("caps", "Bottle caps", "Automatically adjust owned bottle caps. Own at least one first.", "Inventory", "\uE7C1", 999, "999", "F7", NumberKind.UInt16),
        F("items", "Healing items", "Fill owned healing stacks, respecting each item's capacity.", "Inventory", "\uE7B8", 999, "999", "F8", NumberKind.UInt16),
        F("support", "Support items", "Maintain owned support items. Quest items are excluded.", "Inventory", "\uE7B8", 999, "999", "F10", NumberKind.UInt16),
        F("materials", "Materials", "Maintain owned material stacks within their natural limits.", "Inventory", "\uE7B8", 999, "999", "F11", NumberKind.UInt16),
        F("facility", "Facility items", "Maintain owned facility items; bottle caps stay separate.", "Inventory", "\uE7B8", 999, "999", "None", NumberKind.UInt16),
        F("growth", "Growth items", "Maintain owned growth items within their natural limits.", "Inventory", "\uE734", 999, "999", "None", NumberKind.UInt16),
        F("noConsume", "No battle item consumption", "Preserve ordinary healing and support items used in battle, including potions and paintballs. Special and quest items stay original.", "Inventory", "\uE73E", 1, "1", "None", NumberKind.Byte),
        F("fieldItems", "No field item consumption", "Keep items used from the field menu. Uses the separate camp item-use path; shop and crafting deductions stay normal.", "Inventory", "\uE73E", 1, "1", "None", NumberKind.Byte),
        F("loot", "Loot multiplier", "Multiply ordinary gathered materials and enemy material rewards. Enable before gathering or defeating a monster.", "Inventory", "\uE8B7", 10, "2", "None", NumberKind.Int32),
        F("retreat", "100% retreat", "Defeated monsters retreat whenever the game allows it. Boss and quest restrictions still apply. F9 toggles.", "Exploration", "\uE80F",100,"100","F9",NumberKind.UInt16),
        F("radar", "Map radar", "Reveal loaded material, egg and monster markers without a search Monstie. Reapply as areas load.", "Exploration", "\uE707",1,"1","None",NumberKind.Byte),
        F("gatherRange", "4× gathering range", "Reach ordinary materials from four times farther away. Restore normal range when switched off.", "Exploration", "\uE8A9",1,"1","None",NumberKind.Byte),
        F("prayerBuff", "Permanent Prayer Pot effect", "Choose a native prayer effect to maintain. The pot level determines its strength. Switch off to restore your previous prayer.", "Exploration", "\uE734", 8,"1","None"),
        F("fastGather", "Faster manual gathering", "Speed up plant, ore and insect gathering animations at 4×. Press the normal gather button. Chests and eggs stay original.", "Exploration", "\uE768",1,"1","None",NumberKind.Byte),
        F("autoGather", "Auto-collect materials", "Collect nearby plants, insects and ore while Stories 2 has focus. Pauses during dialogue and resumes when field controls return. Eggs and chests stay manual.", "Exploration", "\uE73E",1,"1","None",NumberKind.Byte)
    ]);
    static Feature F(string id, string title, string desc, string cat, string glyph, double max, string target, string key, NumberKind kind = NumberKind.Int32) => new()
    { Id=id,Title=title,Description=desc,Category=cat,Glyph=glyph,Maximum=max,Minimum=id is "enemyHp" or "damage" or "battleExp"||cat=="Movement"?1:0,Target=target,Hotkey=key,Kind=kind };
}
public sealed record Activity(string Time, string Title, string Detail);
public sealed record BackupInfo(string Name, string Path, string Created, string Size);
public sealed record ProfileFeature(string Id, string Target, string Hotkey, bool Favorite);
public sealed record Profile(string Name, double ExperienceMultiplier, List<ProfileFeature> Features);
