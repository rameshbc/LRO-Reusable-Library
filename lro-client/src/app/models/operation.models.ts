export enum OperationState {
  Accepted = 0,
  Running = 1,
  Succeeded = 2,
  Failed = 3,
  Cancelled = 4,
  TimedOut = 5,
}

export const OperationStateLabels: Record<number, string> = {
  [OperationState.Accepted]: 'Accepted',
  [OperationState.Running]: 'Running',
  [OperationState.Succeeded]: 'Succeeded',
  [OperationState.Failed]: 'Failed',
  [OperationState.Cancelled]: 'Cancelled',
  [OperationState.TimedOut]: 'Timed Out',
};

export interface OperationAcceptedResponse {
  operationId: string;
  operationName: string;
  status: OperationState;
  statusCheckUrl: string;
  cancelUrl?: string;
  estimatedDurationSeconds: number;
  createdAtUtc: string;
}

export interface OperationStatusResponse {
  operationId: string;
  operationName: string;
  status: OperationState;
  percentComplete: number;
  createdAtUtc: string;
  lastUpdatedAtUtc: string;
  completedAtUtc?: string;
  resultData?: string;
  errorMessage?: string;
  retryAfterSeconds?: number;
}

export interface ReportRequest {
  reportType: string;
  startDate?: string;
  endDate?: string;
}

export interface ExportRequest {
  format: string;
  filter?: string;
}
