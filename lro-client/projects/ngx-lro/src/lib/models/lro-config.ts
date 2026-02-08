import { InjectionToken } from '@angular/core';

/**
 * Configuration for the ngx-lro library.
 */
export interface LroConfig {
  /** Base URL of the API (e.g. 'http://localhost:5155'). */
  apiBaseUrl: string;

  /**
   * Path prefix for the operations status/cancel/list endpoints.
   * Defaults to '/api/operations'.
   */
  operationsPath?: string;

  /** Default polling interval in milliseconds. Defaults to 2000. */
  defaultPollingIntervalMs?: number;
}

/** Injection token for providing LroConfig. */
export const LRO_CONFIG = new InjectionToken<LroConfig>('LRO_CONFIG');
