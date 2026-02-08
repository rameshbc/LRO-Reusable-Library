import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { LroService, OperationAcceptedResponse } from 'ngx-lro';
import { ReportRequest, ExportRequest } from '../models/operation.models';
import { environment } from '../../environments/environment';

/**
 * App-specific service that delegates all LRO plumbing to `LroService`
 * and adds domain endpoints (reports, exports, etc.).
 */
@Injectable({ providedIn: 'root' })
export class OperationService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(
    private http: HttpClient,
    public readonly lro: LroService,
  ) {}

  /** Start a report generation (long-running) */
  startReportGeneration(request: ReportRequest): Observable<OperationAcceptedResponse> {
    return this.lro.startOperation('/api/reports/generate', request);
  }

  /** Start a data export (long-running) */
  startDataExport(request: ExportRequest): Observable<OperationAcceptedResponse> {
    return this.lro.startOperation('/api/reports/export', request);
  }

  /** Quick summary (normal sync endpoint — not an LRO) */
  getQuickSummary(): Observable<unknown> {
    return this.http.get(`${this.baseUrl}/api/reports/quick-summary`);
  }
}
