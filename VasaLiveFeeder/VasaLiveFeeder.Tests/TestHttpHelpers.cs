using System;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace VasaLiveFeeder.Tests;

// Minimal test implementations to allow calling the Function1.Run method directly.

public class TestHttpRequestData : HttpRequestData
{
    private readonly MemoryStream _bodyStream;
    public TestHttpRequestData(Uri url, string method, string body) : base(null)
    {
        Url = url;
        Method = method;
        _bodyStream = new MemoryStream(Encoding.UTF8.GetBytes(body ?? string.Empty));
        Body = _bodyStream;
        Headers = new HttpHeadersCollection();
    }

    public override Stream Body { get; }
    public override HttpHeadersCollection Headers { get; }
    public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
    public override string Method { get; }
    public override Uri Url { get; }
    public override IEnumerable<ClaimsIdentity> Identities => Array.Empty<ClaimsIdentity>();

    public override HttpResponseData CreateResponse()
    {
        return new TestHttpResponseData(FunctionContext);
    }

    public new HttpResponseData CreateResponse(HttpStatusCode statusCode)
    {
        var r = new TestHttpResponseData(FunctionContext) { StatusCode = statusCode };
        return r;
    }
}

public class TestHttpResponseData : HttpResponseData
{
    private readonly MemoryStream _body = new MemoryStream();

    public TestHttpResponseData(FunctionContext functionContext) : base(functionContext)
    {
        Body = _body;
        Headers = new HttpHeadersCollection();
    }

    public override Stream Body { get; set; }
    public override HttpHeadersCollection Headers { get; set; }
    public override HttpCookies Cookies => null;
    public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    public string GetBodyAsString() => Encoding.UTF8.GetString(_body.ToArray());
}
