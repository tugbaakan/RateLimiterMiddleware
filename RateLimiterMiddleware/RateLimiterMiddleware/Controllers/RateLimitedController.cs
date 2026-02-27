using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Net.Http.Headers;
using System.Text;
using RateLimiterMiddleware.Models;

namespace RateLimiterMiddleware.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class RateLimitedController : ControllerBase
    {

        private readonly IDatabase _db;
        private readonly IConfiguration _config;
        public RateLimitedController(IConnectionMultiplexer mux, IConfiguration config)
        {
            _db = mux.GetDatabase();
            _config = config;
        }

        [HttpGet]
        [HttpPost]
        [Route("limited")]
        public async Task<IActionResult> Limited()
        {
            return new JsonResult(new {Limited = false});
        }
        
        [HttpGet]
        [HttpPost]
        [Route("indirectly-limited")]
        public async Task<IActionResult> IndirectlyLimited()
        {
            return new JsonResult(new {NeverLimited = true});
        }

        



    }
}
