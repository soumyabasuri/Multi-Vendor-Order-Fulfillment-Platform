using Microsoft.EntityFrameworkCore;
using MultiVendorPlatform.Infrastructure.Data.Entities;
using MultiVendorPlatform.Modules.Cart.Domain;
using MultiVendorPlatform.Modules.Catalog.Domain;
using MultiVendorPlatform.Modules.Fulfillment.Domain;
using MultiVendorPlatform.Modules.Identity.Domain;
using MultiVendorPlatform.Modules.Inventory.Domain;
using MultiVendorPlatform.Modules.Notifications.Domain;
using MultiVendorPlatform.Modules.Orders.Domain;
using MultiVendorPlatform.Modules.Payments.Domain;
using MultiVendorPlatform.Modules.Vendors.Domain;

namespace MultiVendorPlatform.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Vendor> Vendors => Set<Vendor>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();

    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<VendorOrder> VendorOrders => Set<VendorOrder>();

    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Identity Configurations
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(u => u.Id);
            b.Property(u => u.Email).HasMaxLength(256).IsRequired();
            b.Property(u => u.NormalizedEmail).HasMaxLength(256).IsRequired();
            b.HasIndex(u => u.NormalizedEmail).IsUnique();
            b.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            b.Property(u => u.LastName).HasMaxLength(100).IsRequired();
            b.Property(u => u.PasswordHash).IsRequired();
            b.HasIndex(u => u.VendorId);
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("roles");
            b.HasKey(r => r.Id);
            b.Property(r => r.Name).HasMaxLength(50).IsRequired();
            b.Property(r => r.NormalizedName).HasMaxLength(50).IsRequired();
            b.HasIndex(r => r.NormalizedName).IsUnique();

            // Seed Roles
            b.HasData(
                new Role { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = Role.Customer, NormalizedName = "CUSTOMER", Description = "Marketplace Customer" },
                new Role { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = Role.Vendor, NormalizedName = "VENDOR", Description = "Vendor Seller" },
                new Role { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = Role.Admin, NormalizedName = "ADMIN", Description = "System Administrator" },
                new Role { Id = Guid.Parse("44444444-4444-4444-4444-444444444444"), Name = Role.Warehouse, NormalizedName = "WAREHOUSE", Description = "Warehouse Operations" }
            );
        });

        modelBuilder.Entity<UserRole>(b =>
        {
            b.ToTable("user_roles");
            b.HasKey(ur => new { ur.UserId, ur.RoleId });
            b.HasOne(ur => ur.User).WithMany(u => u.UserRoles).HasForeignKey(ur => ur.UserId);
            b.HasOne(ur => ur.Role).WithMany(r => r.UserRoles).HasForeignKey(ur => ur.RoleId);
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens");
            b.HasKey(rt => rt.Id);
            b.Property(rt => rt.Token).HasMaxLength(500).IsRequired();
            b.HasIndex(rt => rt.Token).IsUnique();
            b.HasOne(rt => rt.User).WithMany(u => u.RefreshTokens).HasForeignKey(rt => rt.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // 2. Vendor Configurations
        modelBuilder.Entity<Vendor>(b =>
        {
            b.ToTable("vendors", t =>
            {
                t.HasCheckConstraint("CK_Vendors_CommissionRate", "\"CommissionRate\" >= 0 AND \"CommissionRate\" <= 1.0");
            });
            b.HasKey(v => v.Id);
            b.Property(v => v.BusinessName).HasMaxLength(200).IsRequired();
            b.Property(v => v.Slug).HasMaxLength(250).IsRequired();
            b.HasIndex(v => v.Slug).IsUnique();
            b.HasIndex(v => v.OwnerUserId);
            b.HasIndex(v => v.Status);
            b.Property(v => v.CommissionRate).HasPrecision(5, 4);
            b.HasOne(v => v.OwnerUser).WithMany().HasForeignKey(v => v.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        });

        // 3. Catalog Configurations
        modelBuilder.Entity<Category>(b =>
        {
            b.ToTable("categories");
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(150).IsRequired();
            b.Property(c => c.Slug).HasMaxLength(200).IsRequired();
            b.HasIndex(c => c.Slug).IsUnique();
            b.HasOne(c => c.ParentCategory).WithMany(c => c.SubCategories).HasForeignKey(c => c.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("products", t =>
            {
                t.HasCheckConstraint("CK_Products_BasePrice", "\"BasePrice\" >= 0");
            });
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).HasMaxLength(250).IsRequired();
            b.Property(p => p.Slug).HasMaxLength(300).IsRequired();
            b.Property(p => p.Sku).HasMaxLength(100).IsRequired();
            b.Property(p => p.BasePrice).HasPrecision(18, 2);
            b.HasIndex(p => p.Slug).IsUnique();
            b.HasIndex(p => new { p.VendorId, p.Sku }).IsUnique();
            b.HasIndex(p => p.CategoryId);
            b.HasIndex(p => p.IsActive);
            b.HasOne(p => p.Vendor).WithMany().HasForeignKey(p => p.VendorId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProductVariant>(b =>
        {
            b.ToTable("product_variants", t =>
            {
                t.HasCheckConstraint("CK_ProductVariants_Price", "\"Price\" >= 0");
            });
            b.HasKey(pv => pv.Id);
            b.Property(pv => pv.Sku).HasMaxLength(100).IsRequired();
            b.Property(pv => pv.Price).HasPrecision(18, 2);
            b.Property(pv => pv.AttributesJson).HasColumnType("jsonb");
            b.HasIndex(pv => new { pv.ProductId, pv.Sku }).IsUnique();
            b.HasOne(pv => pv.Product).WithMany(p => p.Variants).HasForeignKey(pv => pv.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        // 4. Inventory Configurations (High Concurrency & Constraints)
        modelBuilder.Entity<Inventory>(b =>
        {
            b.ToTable("inventories", t =>
            {
                t.HasCheckConstraint("CK_Inventories_Quantity_NonNegative", "\"Quantity\" >= 0");
                t.HasCheckConstraint("CK_Inventories_Reserved_NonNegative", "\"ReservedQuantity\" >= 0");
                t.HasCheckConstraint("CK_Inventories_Available_Valid", "\"Quantity\" >= \"ReservedQuantity\"");
            });
            b.HasKey(i => i.Id);
            b.Property(i => i.Version).IsConcurrencyToken();
            b.HasIndex(i => new { i.ProductId, i.ProductVariantId }).IsUnique();
            b.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(i => i.ProductVariant).WithMany().HasForeignKey(i => i.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InventoryReservation>(b =>
        {
            b.ToTable("inventory_reservations");
            b.HasKey(ir => ir.Id);
            b.HasIndex(ir => new { ir.OrderId, ir.ProductId });
            b.HasIndex(ir => new { ir.Status, ir.ExpiresAtUtc });
            b.HasOne(ir => ir.Inventory).WithMany(i => i.Reservations).HasForeignKey(ir => ir.InventoryId).OnDelete(DeleteBehavior.Cascade);
        });

        // 5. Cart Configurations
        modelBuilder.Entity<Cart>(b =>
        {
            b.ToTable("carts");
            b.HasKey(c => c.Id);
            b.HasIndex(c => c.CustomerId).IsUnique();
            b.HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CartItem>(b =>
        {
            b.ToTable("cart_items", t =>
            {
                t.HasCheckConstraint("CK_CartItems_Quantity_Positive", "\"Quantity\" > 0");
            });
            b.HasKey(ci => ci.Id);
            b.Property(ci => ci.UnitPrice).HasPrecision(18, 2);
            b.HasIndex(ci => new { ci.CartId, ci.ProductId, ci.ProductVariantId }).IsUnique();
            b.HasOne(ci => ci.Cart).WithMany(c => c.Items).HasForeignKey(ci => ci.CartId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(ci => ci.Product).WithMany().HasForeignKey(ci => ci.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        // 6. Orders Configurations
        modelBuilder.Entity<Order>(b =>
        {
            b.ToTable("orders");
            b.HasKey(o => o.Id);
            b.Property(o => o.OrderNumber).HasMaxLength(50).IsRequired();
            b.HasIndex(o => o.OrderNumber).IsUnique();
            b.HasIndex(o => o.CustomerId);
            b.HasIndex(o => o.Status);
            b.HasIndex(o => o.CreatedAtUtc);
            b.Property(o => o.SubtotalAmount).HasPrecision(18, 2);
            b.Property(o => o.ShippingAmount).HasPrecision(18, 2);
            b.Property(o => o.TaxAmount).HasPrecision(18, 2);
            b.Property(o => o.TotalAmount).HasPrecision(18, 2);
            b.Property(o => o.ShippingAddressJson).HasColumnType("jsonb");
            b.HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.ToTable("order_items");
            b.HasKey(oi => oi.Id);
            b.Property(oi => oi.UnitPrice).HasPrecision(18, 2);
            b.Property(oi => oi.TotalPrice).HasPrecision(18, 2);
            b.HasIndex(oi => oi.OrderId);
            b.HasIndex(oi => oi.VendorId);
            b.HasOne(oi => oi.Order).WithMany(o => o.Items).HasForeignKey(oi => oi.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(oi => oi.Vendor).WithMany().HasForeignKey(oi => oi.VendorId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(oi => oi.Product).WithMany().HasForeignKey(oi => oi.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VendorOrder>(b =>
        {
            b.ToTable("vendor_orders");
            b.HasKey(vo => vo.Id);
            b.Property(vo => vo.SubtotalAmount).HasPrecision(18, 2);
            b.Property(vo => vo.CommissionAmount).HasPrecision(18, 2);
            b.HasIndex(vo => new { vo.OrderId, vo.VendorId });
            b.HasIndex(vo => vo.Status);
            b.HasOne(vo => vo.Order).WithMany(o => o.VendorOrders).HasForeignKey(vo => vo.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(vo => vo.Vendor).WithMany().HasForeignKey(vo => vo.VendorId).OnDelete(DeleteBehavior.Restrict);
        });

        // 7. Payment Configurations
        modelBuilder.Entity<Payment>(b =>
        {
            b.ToTable("payments");
            b.HasKey(p => p.Id);
            b.Property(p => p.Amount).HasPrecision(18, 2);
            b.Property(p => p.Currency).HasMaxLength(10).IsRequired();
            b.Property(p => p.PaymentMethod).HasMaxLength(50).IsRequired();
            b.HasIndex(p => p.OrderId);
            b.HasIndex(p => p.IdempotencyKey).IsUnique();
            b.Property(p => p.ProviderResponseJson).HasColumnType("jsonb");
            b.HasOne(p => p.Order).WithMany().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        // 8. Fulfillment Configurations
        modelBuilder.Entity<Shipment>(b =>
        {
            b.ToTable("shipments");
            b.HasKey(s => s.Id);
            b.Property(s => s.Carrier).HasMaxLength(100).IsRequired();
            b.Property(s => s.TrackingNumber).HasMaxLength(100).IsRequired();
            b.HasIndex(s => s.VendorOrderId);
            b.HasIndex(s => s.TrackingNumber);
            b.Property(s => s.TrackingHistoryJson).HasColumnType("jsonb");
            b.HasOne(s => s.VendorOrder).WithMany().HasForeignKey(s => s.VendorOrderId).OnDelete(DeleteBehavior.Cascade);
        });

        // 9. Notifications Configurations
        modelBuilder.Entity<Notification>(b =>
        {
            b.ToTable("notifications");
            b.HasKey(n => n.Id);
            b.Property(n => n.Title).HasMaxLength(250).IsRequired();
            b.HasIndex(n => new { n.UserId, n.Status });
            b.Property(n => n.MetadataJson).HasColumnType("jsonb");
            b.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        // 10. Infrastructure: Outbox, Idempotency & Audit
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(om => om.Id);
            b.Property(om => om.EventType).HasMaxLength(200).IsRequired();
            b.Property(om => om.Topic).HasMaxLength(150).IsRequired();
            b.Property(om => om.AggregateType).HasMaxLength(100).IsRequired();
            b.Property(om => om.AggregateId).HasMaxLength(100).IsRequired();
            b.Property(om => om.PayloadJson).HasColumnType("jsonb");
            b.HasIndex(om => new { om.Status, om.CreatedAtUtc });
        });

        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("idempotency_records");
            b.HasKey(ir => ir.Id);
            b.Property(ir => ir.IdempotencyKey).HasMaxLength(100).IsRequired();
            b.Property(ir => ir.RequestPath).HasMaxLength(250).IsRequired();
            b.Property(ir => ir.RequestHash).HasMaxLength(128).IsRequired();
            b.Property(ir => ir.ResponseBodyJson).HasColumnType("jsonb");
            b.HasIndex(ir => ir.IdempotencyKey).IsUnique();
            b.HasIndex(ir => ir.ExpiresAtUtc);
        });

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.ToTable("audit_logs");
            b.HasKey(al => al.Id);
            b.Property(al => al.Action).HasMaxLength(100).IsRequired();
            b.Property(al => al.EntityName).HasMaxLength(100).IsRequired();
            b.Property(al => al.EntityId).HasMaxLength(100).IsRequired();
            b.Property(al => al.ChangesJson).HasColumnType("jsonb");
            b.HasIndex(al => new { al.EntityName, al.EntityId });
            b.HasIndex(al => al.CreatedAtUtc);
        });
    }
}
