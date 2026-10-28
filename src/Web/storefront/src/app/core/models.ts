// Mirrors the JSON contracts exposed by the microservices through the API gateway.

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface Product {
  id: string;
  name: string;
  description: string;
  category: string;
  price: number;
  imageUrl: string;
}

export interface CartItem {
  productId: string;
  productName: string;
  imageUrl: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

export interface ShoppingCart {
  userName: string;
  items: CartItem[];
  total: number;
}

export interface ShippingAddress {
  fullName: string;
  street: string;
  city: string;
  country: string;
  zipCode: string;
}

export interface CheckoutResult {
  orderId: string;
  total: number;
}

export type OrderStatus = 'Submitted' | 'Paid' | 'Shipped' | 'Cancelled';

export interface OrderItem {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

export interface Order {
  id: string;
  userName: string;
  items: OrderItem[];
  shippingAddress: ShippingAddress;
  status: OrderStatus;
  createdAt: string;
  total: number;
}
