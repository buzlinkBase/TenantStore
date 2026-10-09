using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Subscriptions;
using TenantStoreApi.Controllers;
using TenantStoreApi.Filters;
using Xunit;

namespace TenantStoreApi.Tests.Platform;

public class PlatformPlansControllerTests
{
    [Fact]
    public void TheCatalog_CanBeWrappedByTheResponseFilter_AsEveryApiResultIs()
    {
        // The catalog is built from a collection expression; handed out as-is its runtime type is a
        // compiler-generated internal class, and the wrapper (which binds the runtime type dynamically) throws -> HTTP 500.
        var controller = new PlatformPlansController(null!);
        var result = controller.Catalog().Should().BeOfType<OkObjectResult>().Subject;

        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var context = new ResultExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), [], result, controller);

        new ResponseWrapperFilter().OnResultExecuting(context);

        var wrapped = context.Result.Should().BeOfType<ObjectResult>().Subject.Value.Should().BeOfType<ResponseModel<List<EntitlementInfo>>>().Subject;
        wrapped.Data.Should().HaveCount(EntitlementCatalog.All.Count);
    }
}
