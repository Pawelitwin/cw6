using Microsoft.EntityFrameworkCore;
using Cw6.Models;
namespace Cw6.Data
{
    public class WarehouseContext : DbContext
    {
        public WarehouseContext(DbContextOptions<WarehouseContext> options) : base(options) 
        {

        }
        public DbSet<Warehouse> Warehouse { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<ProductWarehouse> productWarehouses { get; set; }
    }
}
