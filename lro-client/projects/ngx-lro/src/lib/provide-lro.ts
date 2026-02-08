import { EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import { LRO_CONFIG, LroConfig } from './models/lro-config';

/**
 * Provide the ngx-lro library configuration.
 *
 * Call this in your app's `providers` array (or in `provideRouter`, etc.):
 *
 * ```ts
 * import { provideLro } from 'ngx-lro';
 *
 * export const appConfig: ApplicationConfig = {
 *   providers: [
 *     provideLro({ apiBaseUrl: environment.apiBaseUrl }),
 *   ],
 * };
 * ```
 */
export function provideLro(config: LroConfig): EnvironmentProviders {
  return makeEnvironmentProviders([
    { provide: LRO_CONFIG, useValue: config },
  ]);
}
