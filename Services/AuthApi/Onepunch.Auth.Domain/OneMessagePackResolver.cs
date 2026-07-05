using MessagePack;

namespace Onepunch.Auth.Domain;

[GeneratedMessagePackResolver]
[System.Runtime.CompilerServices.SkipLocalsInit]
public partial class OneMessagePackResolver { }

[MessagePackObject]
public class Generator 
{
    [Key(0)]
    public Guid Id { get; set; }
}