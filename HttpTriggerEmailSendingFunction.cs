using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Company.Function;

public class HttpTriggerEmailSendingFunction
{
    private readonly ILogger<HttpTriggerEmailSendingFunction> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    public HttpTriggerEmailSendingFunction(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<HttpTriggerEmailSendingFunction> logger)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _config = config;
    }


    public record HttpTriggerEmailSendingFunctionRequest(string ToEmail, string Subject, string? HtmlBody, string? TextBody, string? ToName);

    [Function("HttpTriggerEmailSendingFunction")]
    public async Task<HttpResponseData> RunAsync([HttpTrigger(AuthorizationLevel.Function
    , "post", Route = "send-email")] HttpRequestData req)
    {

        try
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            var json = await new StreamReader(req.Body).ReadToEndAsync();
            var input = JsonSerializer.Deserialize<HttpTriggerEmailSendingFunctionRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            if (input is null || string.IsNullOrWhiteSpace(input.ToEmail) || string.IsNullOrWhiteSpace(input.Subject) ||
                (string.IsNullOrWhiteSpace(input.HtmlBody) && string.IsNullOrWhiteSpace(input.TextBody)))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync("Provide ToEmail, Subject, and HtmlBody or TextBody.");
                return bad;
            }

            var token = _config["ZEPTO_TOKEN"];
            var from = _config["ZEPTO_FROM_ADDRESS"];
            var fromName = _config["ZEPTO_FROM_NAME"] ?? "No-Reply";
            var apiUrl = _config["ZEPTO_API_URL"] ?? "https://api.zeptomail.eu/v1.1/email"; ;

            if (string.IsNullOrWhiteSpace(token) ||
                string.IsNullOrWhiteSpace(from))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync("Missing app settings: ZEPTO_TOKEN, ZEPTO_FROM_ADDRESS.");
                return bad;
            }


            var payload = new
            {
                from = new { address = from, name = fromName },
                to = new[]
          {
                new
                {
                    email_address = new
                    {
                        address = input.ToEmail,
                        name = input.ToName ?? input.ToEmail
                    }
                }
            },
                subject = input.Subject,
                htmlbody = input.HtmlBody,
                textbody = input.TextBody
            };

            var client = _httpClientFactory.CreateClient();

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            httpReq.Headers.TryAddWithoutValidation("Accept", "application/json");
            httpReq.Headers.TryAddWithoutValidation("Authorization", token);

            httpReq.Content = new StringContent(
                JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Encoding.UTF8,
                "application/json");

            var zeptoResp = await client.SendAsync(httpReq);
            var zeptoBody = await zeptoResp.Content.ReadAsStringAsync();


            _logger.LogInformation("ZeptoMail response {Status}: {Body}", (int)zeptoResp.StatusCode, zeptoBody);

            var res = req.CreateResponse(zeptoResp.IsSuccessStatusCode ? HttpStatusCode.OK : HttpStatusCode.BadGateway);
            res.Headers.Add("Content-Type", "application/json; charset=utf-8");

            await res.WriteStringAsync(JsonSerializer.Serialize(new
            {
                ok = zeptoResp.IsSuccessStatusCode,
                status = (int)zeptoResp.StatusCode,
                zeptoBody
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            return res;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Unhandled exception in SendEmail function");

            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync($"Error: {e.GetType().Name} - {e.Message}");
            return err;
        }

    }
}