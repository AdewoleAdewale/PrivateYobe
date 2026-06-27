using System.Net.Http;

namespace YIRS
{
    public interface IHttpClientService
    {
        HttpClient GetHttpClient();
    }
}
