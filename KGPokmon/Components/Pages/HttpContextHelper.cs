using Microsoft.AspNetCore.Http;

public static class HttpContextHelper
{
    public static string GetAcceptHeader()
    {
        var context = new HttpContextAccessor().HttpContext;
        return context?.Request.Headers["Accept"].ToString() ?? "text/html";
    }
}
