using MessagePack;

namespace DTR.Models.ValueObjects;

[MessagePackObject]
public partial class MsgPackPackage
{
    [Key(0)]
    public Guid Id { get; set; }
}
