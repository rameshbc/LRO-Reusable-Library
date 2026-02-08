/*
 * Public API Surface of ngx-lro
 */

// Models
export {
  OperationState,
  OperationStateLabels,
  TERMINAL_STATES,
} from './lib/models/operation.models';
export type {
  OperationAcceptedResponse,
  OperationStatusResponse,
  PollingUpdate,
} from './lib/models/operation.models';

// Configuration
export { LRO_CONFIG } from './lib/models/lro-config';
export type { LroConfig } from './lib/models/lro-config';

// Provider
export { provideLro } from './lib/provide-lro';

// Service
export { LroService } from './lib/services/lro.service';
