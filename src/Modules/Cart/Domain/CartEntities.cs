using MultiVendorPlatform.Modules.Catalog.Domain;
using MultiVendorPlatform.Modules.Common.Domain;
using MultiVendorPlatform.Modules.Identity.Domain;

namespace MultiVendorPlatform.Modules.Cart.Domain;

public class Cart : AggregateRoot<Guid>
{
    public Guid CustomerId { get; set; }
    public User Customer { get; set; } = null!;

    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();

    public Cart()
    {
        Id = Guid.NewGuid();
    }
}

public class CartItem : BaseEntity<Guid>
{
    public Guid CartId { get; set; }
    public Cart Cart { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public CartItem()
    {
        Id = Guid.NewGuid();
    }
}
