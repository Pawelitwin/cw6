using Cw6.Data;
using Microsoft.Extensions.Configuration;
using System.Data.SqlClient;

namespace Cw6
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<SqlConnection>(provider =>
            {
                var connectionString = _configuration.GetConnectionString("DefaultConnection");
                return new SqlConnection(connectionString);
            });

            services.AddControllers();
        }
    }
}
