import { CurrencyPipe, DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, booleanAttribute, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Order } from '../../core/models';
import { problemMessage } from '../../core/problem-details';

@Component({
  selector: 'app-order-detail',
  imports: [CurrencyPipe, DatePipe, RouterLink],
  template: `
    <a routerLink="/orders" class="back">← All orders</a>

    @if (placed()) {
      <div class="alert alert--success" role="status">🎉 Thanks! Your order has been placed.</div>
    }

    @if (order.error()) {
      <div class="alert" role="alert">{{ errorMessage() }}</div>
    } @else if (order.hasValue()) {
      @let o = order.value();
      <div class="layout-2col">
        <section class="card">
          <header class="row row--between">
            <h1>Order #{{ o.id.slice(0, 8) }}</h1>
            <span class="status status--{{ o.status.toLowerCase() }}">{{ o.status }}</span>
          </header>
          <p class="muted">Placed {{ o.createdAt | date: 'full' }}</p>

          <table class="table">
            <thead>
              <tr><th>Product</th><th class="num">Qty</th><th class="num">Price</th><th class="num">Total</th></tr>
            </thead>
            <tbody>
              @for (item of o.items; track item.productId) {
                <tr>
                  <td>{{ item.productName }}</td>
                  <td class="num">{{ item.quantity }}</td>
                  <td class="num">{{ item.unitPrice | currency }}</td>
                  <td class="num">{{ item.lineTotal | currency }}</td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr><th colspan="3">Total</th><th class="num">{{ o.total | currency }}</th></tr>
            </tfoot>
          </table>
        </section>

        <aside class="card summary">
          <h2>Ships to</h2>
          @let a = o.shippingAddress;
          <address>
            {{ a.fullName }}<br />{{ a.street }}<br />{{ a.zipCode }} {{ a.city }}<br />{{ a.country }}
          </address>
        </aside>
      </div>
    } @else {
      <div class="card skeleton skeleton--tall"></div>
    }
  `,
})
export class OrderDetail {
  readonly id = input.required<string>();
  readonly placed = input(false, { transform: booleanAttribute });

  protected readonly order = httpResource<Order>(() => `/api/orders/${this.id()}`);
  protected readonly errorMessage = computed(() =>
    this.order.statusCode() === 404 ? 'Order not found.' : problemMessage(this.order.error()),
  );
}
