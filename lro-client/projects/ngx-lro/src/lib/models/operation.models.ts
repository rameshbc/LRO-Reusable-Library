/**
 * Represents the lifecycle states of a long-running operation.
 */
export enum OperationState {
  Accepted = 0,
  Running = 1,
  Succeeded = 2,
  Failed = 3,
  Cancelled = 4,
  TimedOut = 5,
}

/** Human-readable labels for each operation state. */
export const OperationStateLabels: Record<number, string> = {
  [OperationState.Accepted]: 'Accepted',
  [OperationState.Running]: 'Running',
  [OperationState.Succeeded]: 'Succeeded',
  [OperationState.Failed]: 'Failed',
  [OperationState.Cancelled]: 'Cancelled',
  [OperationState.TimedOut]: 'Timed Out',
};

/** Terminal states — polling stops when one of these is reached. */
export const TERMINAL_STATES: ReadonlySet<OperationState> = new Set([
  OperationState.Succeeded,
  OperationState.Failed,
  OperationState.Cancelled,
  OperationState.TimedOut,
]);

/**
 * Response returned by the server when an operation is accepted (HTTP 202).
 */
export interface OperationAcceptedResponse {
  operationId: string;
  operationName: string;
  status: OperationState;
  statusCheckUrl: string;
  cancelUrl?: string;
  estimatedDurationSeconds: number;
  createdAtUtc: string;
}

/**
 * Response returned when checking the status of an operation.
 */
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

/**
 * Emitted by the polling stream on every tick.
 */
export interface PollingUpdate {
  operationId: string;
  status: OperationStatusResponse;
  isComplete: boolean;
}
