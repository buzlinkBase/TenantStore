namespace DTR.Core;

public interface IBaseModel
{
    public Guid Id { get; set; }

}
public class BaseModel : IBaseModel
{
    public Guid Id { get; set; }
}
