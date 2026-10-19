export interface ApiResponse<T> {
  success: boolean;
  data: T;
  errorMessage?: string;
  errorCode?: string;
  correlationId?: string;
  timestampUtc: string;
}

export interface PagedResult<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface UserDto {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string;
  roles: string[];
  vendorId?: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
  user: UserDto;
}

export interface ProductVariantDto {
  id: string;
  productId: string;
  name: string;
  sku: string;
  price: number;
  attributesJson: string;
  isActive: boolean;
  availableStock: number;
}

export interface ProductDto {
  id: string;
  vendorId: string;
  vendorName: string;
  categoryId: string;
  categoryName: string;
  name: string;
  slug: string;
  description: string;
  sku: string;
  basePrice: number;
  isActive: boolean;
  availableStock: number;
  variants: ProductVariantDto[];
  createdAtUtc: string;
}

export interface CategoryDto {
  id: string;
  name: string;
  slug: string;
  description?: string;
  parentCategoryId?: string;
}

export interface CartItemDto {
  id: string;
  productId: string;
  productName: string;
  productSku: string;
  productVariantId?: string;
  variantName?: string;
  vendorId: string;
  vendorName: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
  isAvailable: boolean;
  availableStock: number;
}

export interface CartDto {
  id: string;
  customerId: string;
  items: CartItemDto[];
  subtotal: number;
  totalItems: number;
  hasStockIssues: boolean;
}

export interface ShippingAddressDto {
  street: string;
  city: string;
  state: string;
  postalCode: string;
  country: string;
  recipientName: string;
  recipientPhone: string;
}

export interface OrderItemDto {
  id: string;
  vendorId: string;
  vendorName: string;
  productId: string;
  productName: string;
  sku: string;
  productVariantId?: string;
  quantity: number;
  unitPrice: number;
  totalPrice: number;
}

export interface VendorOrderDto {
  id: string;
  vendorId: string;
  vendorName: string;
  status: string;
  subtotalAmount: number;
  commissionAmount: number;
  payoutAmount: number;
  trackingNumber?: string;
  shippedAtUtc?: string;
  deliveredAtUtc?: string;
  items: OrderItemDto[];
}

export interface OrderDto {
  id: string;
  orderNumber: string;
  customerId: string;
  status: string;
  subtotalAmount: number;
  shippingAmount: number;
  taxAmount: number;
  totalAmount: number;
  shippingAddress: ShippingAddressDto;
  cancellationReason?: string;
  createdAtUtc: string;
  vendorOrders: VendorOrderDto[];
  items: OrderItemDto[];
}

export interface OrderSummaryDto {
  id: string;
  orderNumber: string;
  customerId: string;
  status: string;
  totalAmount: number;
  totalItems: number;
  totalVendors: number;
  createdAtUtc: string;
}

export interface VendorDto {
  id: string;
  ownerUserId: string;
  businessName: string;
  slug: string;
  description: string;
  contactEmail: string;
  contactPhone?: string;
  status: string;
  commissionRate: number;
  rejectionReason?: string;
  approvedAtUtc?: string;
  createdAtUtc: string;
}

export interface TrackingEventDto {
  status: string;
  location: string;
  note: string;
  timestampUtc: string;
}

export interface ShipmentDto {
  id: string;
  vendorOrderId: string;
  orderId: string;
  vendorId: string;
  carrier: string;
  trackingNumber: string;
  status: string;
  shippedAtUtc?: string;
  estimatedDeliveryUtc?: string;
  actualDeliveryUtc?: string;
  trackingHistory: TrackingEventDto[];
  createdAtUtc: string;
}

export interface NotificationDto {
  id: string;
  userId: string;
  type: string;
  title: string;
  message: string;
  status: string;
  createdAtUtc: string;
  sentAtUtc?: string;
  readAtUtc?: string;
}
