import { Component, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject } from 'rxjs';
import { OperationService, PollingUpdate } from '../services/operation.service';
import {
  OperationAcceptedResponse,
  OperationStatusResponse,
  OperationState,
  OperationStateLabels,
} from '../models/operation.models';

interface TrackedOperation {
  operationId: string;
  operationName: string;
  accepted: OperationAcceptedResponse;
  currentStatus?: OperationStatusResponse;
  isPolling: boolean;
  cancel$: Subject<void>;
  resultParsed?: any;
  logs: string[];
}

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardComponent implements OnDestroy {
  // Form inputs
  reportType = 'Monthly';
  exportFormat = 'CSV';

  // Tracked operations
  operations: TrackedOperation[] = [];

  // All operations from server
  allOperations: OperationStatusResponse[] = [];

  // State label helper
  stateLabels = OperationStateLabels;
  OperationState = OperationState;

  // Loading flags
  isStartingReport = false;
  isStartingExport = false;
  isLoadingList = false;

  constructor(private operationService: OperationService) {
    this.refreshList();
  }

  ngOnDestroy(): void {
    // Stop all polling
    this.operations.forEach((op) => op.cancel$.next());
  }

  /** Start a report generation */
  startReport(): void {
    this.isStartingReport = true;
    this.operationService
      .startReportGeneration({ reportType: this.reportType })
      .subscribe({
        next: (accepted) => {
          this.isStartingReport = false;
          this.trackOperation(accepted);
        },
        error: (err) => {
          this.isStartingReport = false;
          alert('Failed to start report: ' + err.message);
        },
      });
  }

  /** Start a data export */
  startExport(): void {
    this.isStartingExport = true;
    this.operationService
      .startDataExport({ format: this.exportFormat })
      .subscribe({
        next: (accepted) => {
          this.isStartingExport = false;
          this.trackOperation(accepted);
        },
        error: (err) => {
          this.isStartingExport = false;
          alert('Failed to start export: ' + err.message);
        },
      });
  }

  /** Track an accepted operation and start polling */
  private trackOperation(accepted: OperationAcceptedResponse): void {
    const cancel$ = new Subject<void>();
    const tracked: TrackedOperation = {
      operationId: accepted.operationId,
      operationName: accepted.operationName,
      accepted,
      isPolling: true,
      cancel$,
      logs: [`${this.timestamp()} Operation accepted (${accepted.operationName})`],
    };

    this.operations.unshift(tracked);

    // Start polling
    this.operationService
      .pollUntilComplete(accepted.operationId, 1500, cancel$)
      .subscribe({
        next: (update: PollingUpdate) => {
          tracked.currentStatus = update.status;
          tracked.logs.push(
            `${this.timestamp()} ${this.stateLabels[update.status.status]} — ${update.status.percentComplete}%`
          );

          if (update.isComplete) {
            tracked.isPolling = false;

            if (update.status.status === OperationState.Succeeded && update.status.resultData) {
              try {
                tracked.resultParsed = JSON.parse(update.status.resultData);
              } catch {
                tracked.resultParsed = update.status.resultData;
              }
              tracked.logs.push(`${this.timestamp()} ✅ Result received!`);
            } else if (update.status.status === OperationState.Failed) {
              tracked.logs.push(`${this.timestamp()} ❌ Error: ${update.status.errorMessage}`);
            } else if (update.status.status === OperationState.Cancelled) {
              tracked.logs.push(`${this.timestamp()} ⚠️ Operation was cancelled`);
            }

            this.refreshList();
          }
        },
        error: (err) => {
          tracked.isPolling = false;
          tracked.logs.push(`${this.timestamp()} ❌ Polling error: ${err.message}`);
        },
      });
  }

  /** Cancel a tracked operation */
  cancelOperation(tracked: TrackedOperation): void {
    this.operationService.cancelOperation(tracked.operationId).subscribe({
      next: () => {
        tracked.cancel$.next();
        tracked.isPolling = false;
        tracked.logs.push(`${this.timestamp()} Cancellation requested`);
        this.refreshList();
      },
      error: (err) => {
        tracked.logs.push(`${this.timestamp()} Cancel failed: ${err.message}`);
      },
    });
  }

  /** Refresh server-side operations list */
  refreshList(): void {
    this.isLoadingList = true;
    this.operationService.listOperations().subscribe({
      next: (list) => {
        this.allOperations = list;
        this.isLoadingList = false;
      },
      error: () => {
        this.isLoadingList = false;
      },
    });
  }

  /** Get CSS class for state badge */
  getStateBadgeClass(state: OperationState): string {
    switch (state) {
      case OperationState.Accepted: return 'badge-accepted';
      case OperationState.Running: return 'badge-running';
      case OperationState.Succeeded: return 'badge-succeeded';
      case OperationState.Failed: return 'badge-failed';
      case OperationState.Cancelled: return 'badge-cancelled';
      case OperationState.TimedOut: return 'badge-timedout';
      default: return '';
    }
  }

  private timestamp(): string {
    return new Date().toLocaleTimeString();
  }
}
