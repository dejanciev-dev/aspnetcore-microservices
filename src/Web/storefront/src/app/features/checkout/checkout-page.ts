import { CurrencyPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormField, form, maxLength, required, submit } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { BasketStore } from '../../core/basket-store';
import { ShippingAddress } from '../../core/models';
import { problemMessage } from '../../core/problem-details';

type FieldKey = keyof ShippingAddress;

/** Checkout built with Angular Signal Forms: the model is a plain signal, validation is a schema. */
@Component({
  selector: 'app-checkout-page',
  imports: [CurrencyPipe, FormField, RouterLink],
  template: `
    <h1>Checkout</h1>

    @if (basket.cart().items.length === 0) {
      <div class="empty card">
        <p>There is nothing to check out.</p>
        <a class="btn btn--primary" routerLink="/products">Browse products</a>
      </div>
    } @else {
      <div class="layout-2col">
        <form class="card form" (submit)="$event.preventDefault(); placeOrder()" novalidate>
          <h2>Shipping address</h2>

          @for (field of fields; track field.key) {
            @let state = address[field.key]();
            <label class="field" [class.field--invalid]="state.touched() && state.invalid()">
              <span>{{ field.label }}</span>
              <input [formField]="address[field.key]" [autocomplete]="field.autocomplete" />
              @if (state.touched()) {
                @for (error of state.errors(); track error.kind) {
                  <small class="field__error">{{ error.message }}</small>
                }
              }
            </label>
          }

          @if (serverError()) {
            <div class="alert" role="alert">{{ serverError() }}</div>
          }

          <button class="btn btn--primary btn--block" type="submit" [disabled]="address().submitting()">
            {{ address().submitting() ? 'Placing order…' : 'Place order · ' + (basket.cart().total | currency) }}
          </button>
        </form>

        <aside class="card summary">
          <h2>Order summary</h2>
          @for (item of basket.cart().items; track item.productId) {
            <div class="summary__row">
              <span>{{ item.quantity }} × {{ item.productName }}</span>
              <span>{{ item.lineTotal | currency }}</span>
            </div>
          }
          <div class="summary__row summary__total">
            <span>Total</span><span>{{ basket.cart().total | currency }}</span>
          </div>
        </aside>
      </div>
    }
  `,
})
export class CheckoutPage {
  protected readonly basket = inject(BasketStore);
  private readonly router = inject(Router);

  protected readonly fields: { key: FieldKey; label: string; autocomplete: string }[] = [
    { key: 'fullName', label: 'Full name', autocomplete: 'name' },
    { key: 'street', label: 'Street', autocomplete: 'street-address' },
    { key: 'city', label: 'City', autocomplete: 'address-level2' },
    { key: 'zipCode', label: 'ZIP / Postal code', autocomplete: 'postal-code' },
    { key: 'country', label: 'Country', autocomplete: 'country-name' },
  ];

  private readonly model = signal<ShippingAddress>({ fullName: '', street: '', city: '', country: '', zipCode: '' });

  protected readonly address = form(this.model, (path) => {
    for (const { key, label } of this.fields) {
      required(path[key], { message: `${label} is required.` });
      maxLength(path[key], 100, { message: `${label} is too long.` });
    }
  });

  protected readonly serverError = signal<string | null>(null);

  protected placeOrder(): Promise<boolean> {
    this.serverError.set(null);
    return submit(this.address, async (field) => {
      try {
        const result = await this.basket.checkout(field().value());
        await this.router.navigate(['/orders', result.orderId], { queryParams: { placed: true } });
      } catch (error) {
        this.serverError.set(problemMessage(error));
      }
      return undefined;
    });
  }
}
