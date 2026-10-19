using MultiVendorPlatform.Modules.Common.Domain;
using MultiVendorPlatform.Modules.Vendors.Domain;

namespace MultiVendorPlatform.Modules.Catalog.Domain;

public class Category : BaseEntity<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public ICollection<Category> SubCategories { get; set; } = new List<Category>();
    public ICollection<Product> Products { get; set; } = new List<Product>();

    public Category()
    {
        Id = Guid.NewGuid();
    }
}

public class Product : AggregateRoot<Guid>
{
    public Guid VendorId { get; set; }
    public Vendor Vendor { get; set; } = null!;

    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();

    public Product()
    {
        Id = Guid.NewGuid();
    }
}

public class ProductVariant : BaseEntity<Guid>
{
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string Name { get; set; } = string.Empty; // e.g. "Color: Blue, Size: XL"
    public string Sku { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string AttributesJson { get; set; } = "{}"; // JSON dictionary of variant attributes
    public bool IsActive { get; set; } = true;

    public ProductVariant()
    {
        Id = Guid.NewGuid();
    }
}
