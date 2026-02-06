import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, Subject, timer, switchMap, takeWhile, tap, finalize, takeUntil } from 'rxjs';
import {
  OperationAcceptedResponse,
  OperationStatusResponse,
  OperationState,
  ReportRequest,
  ExportRequest,
} from '../models/operation.models';
import { environment } from '../../environments/environment';

export interface PollingUpdate {
  operationId: string;
  status: OperationStatusResponse;
  isComplete: boolean;
}

@Injectable({ providedIn: 'root' })
export class OperationService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  /** Start a report generation (long-running) */
  startReportGeneration(request: ReportRequest): Observable<OperationAcceptedResponse> {
    return this.http.post<OperationAcceptedResponse>(
      `${this.baseUrl}/api/reports/generate`,
      request
    );
  }

  /** Start a data export (long-running) */
  startDataExport(request: ExportRequest): Observable<OperationAcceptedResponse> {
    return this.http.post<OperationAcceptedResponse>(
      `${this.baseUrl}/api/reports/export`,
      request
    );
  }

  /** Get operation status by ID */
  getStatus(operationId: string): Observable<OperationStatusResponse> {
    return this.http.get<OperationStatusResponse>(
      `${this.baseUrl}/api/operations/${operationId}`
    );
  }

  /** Cancel an operation */
  cancelOperation(operationId: string): Observable<any> {
    return this.http.post(`${this.baseUrl}/api/operations/${operationId}/cancel`, null);
  }

  /** List all operations */
  listOperations(operationName?: string, state?: OperationState): Observable<OperationStatusResponse[]> {
    let url = `${this.baseUrl}/api/operations?pageSize=50`;
    if (operationName) url += `&operationName=${operationName}`;
    if (state !== undefined) url += `&state=${state}`;
    return this.http.get<OperationStatusResponse[]>(url);
  }

  /** Quick summary (normal sync endpoint) */
  getQuickSummary(): Observable<any> {
    return this.http.get(`${this.baseUrl}/api/reports/quick-summary`);
  }

  /**
   * Poll an operation until it completes.
   * Emits PollingUpdate on each poll tick.
   * Completes when the operation reaches a terminal state.
   */
  pollUntilComplete(operationId: string, intervalMs = 2000, cancel$?: Subject<void>): Observable<PollingUpdate> {
    const poll$ = timer(0, intervalMs).pipe(
      switchMap(() => this.getStatus(operationId)),
      tap((status) => {
        // Log polling for debugging
        console.log(`[Poll] ${operationId} → ${OperationState[status.status]} (${status.percentComplete}%)`);
      }),
      // Map to PollingUpdate, determine if complete
      switchMap((status) => {
        const isComplete = [
          OperationState.Succeeded,
          OperationState.Failed,
          OperationState.Cancelled,
          OperationState.TimedOut,
        ].includes(status.status);

        return [{ operationId, status, isComplete } as PollingUpdate];
      }),
      // Keep emitting while not complete, but also emit the final complete update
      takeWhile((update) => !update.isComplete, true)
    );

    // If a cancel subject is provided, use it to stop polling
    if (cancel$) {
      return poll$.pipe(takeUntil(cancel$));
    }
    return poll$;
  }
}
