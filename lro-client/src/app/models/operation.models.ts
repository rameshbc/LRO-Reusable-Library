/**
 * Re-export all LRO core models from the shared library.
 * App-specific request/response types live below.
 */
export {
  OperationState,
  OperationStateLabels,
  TERMINAL_STATES,
} from 'ngx-lro';
export type {
  OperationAcceptedResponse,
  OperationStatusResponse,
  PollingUpdate,
} from 'ngx-lro';

// ── App-specific request models ──────────────────────────────────────

export interface ReportRequest {
  reportType: string;
  startDate?: string;
  endDate?: string;
}

export interface ExportRequest {
  format: string;
  filter?: string;
}
