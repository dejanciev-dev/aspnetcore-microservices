import { Injectable, effect, signal } from '@angular/core';

const STORAGE_KEY = 'storefront.userName';
const DEFAULT_USER = 'demo-user';

/**
 * The demo has no identity provider yet, so the "user" is just a name kept in
 * localStorage. Basket and Ordering are keyed by it.
 */
@Injectable({ providedIn: 'root' })
export class UserSession {
  readonly userName = signal(read() ?? DEFAULT_USER);

  constructor() {
    effect(() => write(this.userName()));
  }

  rename(name: string): void {
    const trimmed = name.trim();
    if (trimmed) this.userName.set(trimmed);
  }
}

function read(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

function write(value: string): void {
  try {
    localStorage.setItem(STORAGE_KEY, value);
  } catch {
    // Storage can be unavailable (private mode, SSR); the session still works in memory.
  }
}
