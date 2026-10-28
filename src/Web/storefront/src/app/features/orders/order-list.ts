import { CurrencyPipe, DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Order } from '../../core/models';
import { problemMessage } from '../../core/problem-details';
import { UserSession } from '../../core/user-session';

@Component({
  selector: 'app-order-list',
  imports: [CurrencyPipe, DatePipe, RouterLink],
  template: `
    <h1>Orders for {{ session.userName() }}</h1>

    @if (orders.error()) {
      <div class="alert" role="alert">{{ errorMessage() }}</div>
    } @else if (orders.hasValue()) {
      @if (orders.value().length === 0) {
        <div class="empty card">
          <p>No orders yet.</p>
          <a class="btn btn--primary" routerLink="/products">Start shopping</a>
        </div>
      } @else {
        <div class="card table-wrap">
          <table class="table">
            <thead>
              <tr><th>Order</th><th>Placed</th><th>Items</th><th>Status</th><th class="num">Total</th></tr>
            </thead>
            <tbody>
              @for (order of orders.value(); track order.id) {
                <tr>
                  <td><a [routerLink]="['/orders', order.id]">#{{ order.id.slice(0, 8) }}</a></td>
                  <td>{{ order.createdAt | date: 'medium' }}</td>
                  <td>{{ order.items.length }}</td>
                  <td><span class="status status--{{ order.status.toLowerCase() }}">{{ order.status }}</span></td>
                  <td class="num">{{ order.total | currency }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    } @else {
      <div class="card skeleton"></div>
    }
  `,
})
export class OrderList {
  protected readonly session = inject(UserSession);

  protected readonly orders = httpResource<Order[]>(() => ({
    url: '/api/orders',
    params: { userName: this.session.userName() },
  }));

  protected readonly errorMessage = computed(() => problemMessage(this.orders.error()));
}
