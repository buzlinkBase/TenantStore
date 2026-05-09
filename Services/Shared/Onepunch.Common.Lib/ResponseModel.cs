using MessagePack;

namespace Onepunch.Common.Lib;

public class PaginatedResult<T>
{
    public T? Data { get; set; }
    public PaginationMetaData MetaData { get; set; }
}

[MessagePackObject]
public class ResponseModel<T>
{
    [Key(0)]
    public string? Message { get; set; } = "Success";

    [Key(1)]
    public int? Status { get; set; } = 200;

    [Key(2)]
    public T? Data { get; set; }
}

public class PaginationMetaData
{
    public PaginationMetaData(int totalRecordCount, int page, int? limit)
    {
        var itemsPerPage = limit.GetValueOrDefault();
        TotalCount = totalRecordCount;
        CurrentPage = page;
        TotalPages = itemsPerPage == 0 ? 1 : (int)Math.Ceiling(totalRecordCount / (double)itemsPerPage);
    }

    public int CurrentPage { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasPrevious => CurrentPage > 1;
    public bool HasNext => CurrentPage < TotalPages;
}
