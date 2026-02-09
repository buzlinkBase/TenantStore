 
using System.ComponentModel.DataAnnotations.Schema;

namespace DTR.Models;

public abstract class BaseEntity : EntityBase, IReadonlyAware
{
    [NotMapped]
    public bool IsReadOnly { get; set; }
    [NotMapped]
    public override string EntityType { get => GetType().Name; set => base.EntityType = value; }
}


public interface IReadonlyAware
{
    bool IsReadOnly { get; set; }
}
