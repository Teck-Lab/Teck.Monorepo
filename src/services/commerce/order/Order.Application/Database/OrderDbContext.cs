using Microsoft.EntityFrameworkCore;
using Order.Domain.Entities;

namespace Order.Host.Database;

public class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}
