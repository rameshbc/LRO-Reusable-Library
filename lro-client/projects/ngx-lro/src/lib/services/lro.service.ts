import { Injectable, Inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, Subject, timer, switchMap, takeWhile, tap, takeUntil, map } from 'rxjs';

import { LRO_CONFIG, LroConfig } from '../models/lro-config';
import {
  OperationAcceptedResponse,
  OperationStatusResponse,
  OperationState,
  OperationStateLabels,
  PollingUpdate,
  TERMINAL_STATES,
} from '../models/operation.models';

/**
 * Generic service for managing long-running operations.
 *
 * Responsibilities:
 *   - Initiating any long-running operation via an arbitrary POST endpoint
 *   - Polling the shared /operations/:id status endpoint
 *   - Cancelling operations
 *   - Listing operations with optional filters
 *
 * The service is **not** tied to a specific domain (reports, exports, etc.).
 * Consumer apps call `startOperation()` with their own endpoint and payload.
 */
@Injectable({ providedIn: 'root' })
export class LroService {
  private readonly apiBaseUrl: string;
  private readonly operationsPath: string;
  private readonly defaultPollingIntervalMs: number;

  constructor(
    private http: HttpClient,
    @Inject(LRO_CONFIG) config: LroConfig,
  ) {
    this.apiBaseUrl = config.apiBaseUrl.replace(/\/+$/, '');
    this.operationsPath = config.operationsPath ?? '/api/operations';
    this.defaultPollingIntervalMs = config.defaultPollingIntervalMs ?? 2000;
  }

  // ─── Generic Operation Starter ──────────────────────────────────────

  /**
   * Start any long-running operation by POSTing to the given path.
   *
   * @param path   - Relative API path (e.g. '/api/reports/generate').
   * @param body   - Request payload (type-safe per caller).
   * @returns Observable emitting the 202 Accepted response.
   *
   * @example
   * lroService.startOperation<ReportRequest>('/api/reports/generate', { reportType: 'Monthly' });
   */
  startOperation<TBody = unknown>(path: string, body: TBody): Observable<OperationAcceptedResponse> {
    return this.http.post<OperationAcceptedResponse>(`${this.apiBaseUrl}${path}`, body);
  }

  // ─── Operation Status ───────────────────────────────────────────────

  /** Get the current status of an operation by ID. */
  getStatus(operationId: string): Observable<OperationStatusResponse> {
    return this.http.get<OperationStatusResponse>(
      `${this.apiBaseUrl}${this.operationsPath}/${operationId}`,
    );
  }

  // ─── Cancel ─────────────────────────────────────────────────────────

  /** Request cancellation of an operation. */
  cancelOperation(operationId: string): Observable<unknown> {
    return this.http.post(
      `${this.apiBaseUrl}${this.operationsPath}/${operationId}/cancel`,
      null,
    );
  }

  // ─── List ───────────────────────────────────────────────────────────

  /**
   * List operations, optionally filtered by name and/or state.
   *
   * @param pageSize      - Max items to return (default 50).
   * @param operationName - Filter by operation name.
   * @param state         - Filter by current state.
   */
  listOperations(
    pageSize = 50,
    operationName?: string,
    state?: OperationState,
  ): Observable<OperationStatusResponse[]> {
    let url = `${this.apiBaseUrl}${this.operationsPath}?pageSize=${pageSize}`;
    if (operationName) url += `&operationName=${encodeURIComponent(operationName)}`;
    if (state !== undefined) url += `&state=${state}`;
    return this.http.get<OperationStatusResponse[]>(url);
  }

  // ─── Polling ────────────────────────────────────────────────────────

  /**
   * Poll an operation until it reaches a terminal state.
   *
   * Emits a `PollingUpdate` on every tick (including the final one).
   * The stream completes once the operation is complete.
   *
   * @param operationId - The operation to poll.
   * @param intervalMs  - Polling interval (defaults to config value).
   * @param cancel$     - Optional subject; emit to stop polling early.
   */
  pollUntilComplete(
    operationId: string,
    intervalMs?: number,
    cancel$?: Subject<void>,
  ): Observable<PollingUpdate> {
    const interval = intervalMs ?? this.defaultPollingIntervalMs;

    const poll$ = timer(0, interval).pipe(
      switchMap(() => this.getStatus(operationId)),
      tap((status) => {
        console.log(
          `[LRO Poll] ${operationId} → ${OperationStateLabels[status.status]} (${status.percentComplete}%)`,
        );
      }),
      map((status) => {
        const isComplete = TERMINAL_STATES.has(status.status);
        return { operationId, status, isComplete } as PollingUpdate;
      }),
      takeWhile((update) => !update.isComplete, true),
    );

    return cancel$ ? poll$.pipe(takeUntil(cancel$)) : poll$;
  }
}
