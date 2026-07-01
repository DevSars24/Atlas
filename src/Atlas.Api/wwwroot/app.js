document.addEventListener('DOMContentLoaded', () => {
    // DOM Elements
    const valWorkers = document.getElementById('val-workers');
    const valPending = document.getElementById('val-pending');
    const valProcessing = document.getElementById('val-processing');
    const valSucceeded = document.getElementById('val-succeeded');
    const valFailed = document.getElementById('val-failed');
    const valDlq = document.getElementById('val-dlq');

    const jobMsgInput = document.getElementById('job-msg');
    const btnEnqueueSuccess = document.getElementById('btn-enqueue-success');
    const btnEnqueueFail = document.getElementById('btn-enqueue-fail');
    const btnRefresh = document.getElementById('btn-refresh');
    const actionStatus = document.getElementById('action-status');

    const jobsList = document.getElementById('jobs-list');
    const detailsPanel = document.getElementById('details-panel');
    const detailsJobId = document.getElementById('details-job-id');
    const btnCloseDetails = document.getElementById('btn-close-details');

    const detQueue = document.getElementById('det-queue');
    const detType = document.getElementById('det-type');
    const detPriority = document.getElementById('det-priority');
    const detAttempts = document.getElementById('det-attempts');
    const detIdemKey = document.getElementById('det-idem-key');
    const detCreated = document.getElementById('det-created');
    const detError = document.getElementById('det-error');
    const detLogs = document.getElementById('det-logs');

    let selectedJobId = null;

    // Fetch and update dashboard stats and jobs feed
    async function updateDashboard() {
        try {
            // 1. Fetch Workers
            const workersRes = await fetch('/api/workers');
            if (workersRes.ok) {
                const workers = await workersRes.json();
                const activeWorkers = workers.filter(w => w.status === 'Active' || w.status === 1);
                valWorkers.textContent = activeWorkers.length;
            }

            // 2. Fetch Jobs
            const jobsRes = await fetch('/api/jobs?page=1&pageSize=50');
            if (jobsRes.ok) {
                const jobs = await jobsRes.json();
                renderJobsList(jobs);
                updateStats(jobs);
            }
        } catch (error) {
            console.error('Error polling dashboard stats:', error);
        }
    }

    // Process stats calculations
    function updateStats(jobs) {
        let pending = 0;
        let processing = 0;
        let succeeded = 0;
        let failed = 0;
        let dlq = 0;

        jobs.forEach(job => {
            const status = job.status.toString().toLowerCase();
            if (status === 'pending' || status === '0') pending++;
            else if (status === 'processing' || status === '1') processing++;
            else if (status === 'succeeded' || status === '2') succeeded++;
            else if (status === 'failed' || status === '3') failed++;
            else if (status === 'deadlettered' || status === '4') dlq++;
        });

        valPending.textContent = pending;
        valProcessing.textContent = processing;
        valSucceeded.textContent = succeeded;
        valFailed.textContent = failed;
        valDlq.textContent = dlq;
    }

    // Render Jobs Feed
    function renderJobsList(jobs) {
        if (jobs.length === 0) {
            jobsList.innerHTML = '<div class="no-jobs">No jobs found in the system. Use the Control Panel to enqueue a job.</div>';
            return;
        }

        jobsList.innerHTML = '';
        jobs.forEach(job => {
            const item = document.createElement('div');
            item.className = 'job-item';
            item.dataset.id = job.id;

            const statusClass = getStatusClass(job.status);
            const statusText = getStatusText(job.status);

            const scheduledTime = new Date(job.scheduledAt).toLocaleTimeString();

            item.innerHTML = `
                <div class="job-meta">
                    <div class="job-id-type">
                        <span class="job-type-label">${job.jobType}</span>
                        <span class="job-id-short">#${job.id.substring(0, 8)}...</span>
                    </div>
                    <div class="job-time-attempts">
                        Scheduled: ${scheduledTime} | Attempts: ${job.attempts}/${job.maxAttempts}
                    </div>
                </div>
                <span class="job-status-badge ${statusClass}">${statusText}</span>
            `;

            item.addEventListener('click', () => showJobDetails(job.id));
            jobsList.appendChild(item);
        });
    }

    function getStatusClass(status) {
        const s = status.toString().toLowerCase();
        if (s === 'pending' || s === '0') return 'status-pending';
        if (s === 'processing' || s === '1') return 'status-processing';
        if (s === 'succeeded' || s === '2') return 'status-succeeded';
        if (s === 'failed' || s === '3') return 'status-failed';
        if (s === 'deadlettered' || s === '4') return 'status-deadlettered';
        return '';
    }

    function getStatusText(status) {
        const s = status.toString().toLowerCase();
        if (s === 'pending' || s === '0') return 'Pending';
        if (s === 'processing' || s === '1') return 'Processing';
        if (s === 'succeeded' || s === '2') return 'Succeeded';
        if (s === 'failed' || s === '3') return 'Failed';
        if (s === 'deadlettered' || s === '4') return 'Dead-Lettered';
        return status;
    }

    // Submit / Enqueue Job
    async function enqueueJob(fail = false) {
        const message = jobMsgInput.value.trim() || "Default message";
        const idempotencyKey = "key-" + Date.now(); // random mock key for testing

        const payload = JSON.stringify({
            Message: message,
            Fail: fail
        });

        const reqBody = {
            queue: "default",
            jobType: "TestJob",
            payload: payload,
            priority: 1, // Normal
            idempotencyKey: idempotencyKey,
            maxAttempts: 3
        };

        showActionStatus('Submitting job request...', 'info');

        try {
            const res = await fetch('/api/jobs', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(reqBody)
            });

            if (res.ok) {
                const job = await res.json();
                showActionStatus(`Job successfully enqueued! ID: ${job.id}`, 'success');
                updateDashboard();
            } else {
                const errText = await res.text();
                showActionStatus(`Enqueuing failed: ${errText}`, 'error');
            }
        } catch (error) {
            showActionStatus(`Connection error: ${error.message}`, 'error');
        }
    }

    function showActionStatus(msg, type) {
        actionStatus.textContent = msg;
        actionStatus.className = 'action-status';
        if (type === 'success') actionStatus.classList.add('success');
        else if (type === 'error') actionStatus.classList.add('error');
        else actionStatus.classList.add('info');
        actionStatus.classList.remove('hidden');

        // Hide success message after 5 seconds
        if (type === 'success' || type === 'info') {
            setTimeout(() => {
                actionStatus.classList.add('hidden');
            }, 5000);
        }
    }

    // View Job Details
    async function showJobDetails(id) {
        selectedJobId = id;
        detailsJobId.textContent = id;
        detailsPanel.classList.remove('hidden');

        try {
            const res = await fetch(`/api/jobs/${id}`);
            if (res.ok) {
                const job = await res.json();
                
                detQueue.textContent = job.queue;
                detType.textContent = job.jobType;
                detPriority.textContent = getPriorityText(job.priority);
                detAttempts.textContent = `${job.attempts} / ${job.maxAttempts}`;
                detIdemKey.textContent = job.idempotencyKey || 'N/A';
                detCreated.textContent = new Date(job.createdAt).toLocaleString();
                
                if (job.lastError) {
                    detError.textContent = job.lastError;
                    detError.parentElement.classList.remove('hidden');
                } else {
                    detError.parentElement.classList.add('hidden');
                }

                // Render Logs console
                renderLogs(job.logs || []);
            }
        } catch (error) {
            console.error('Error fetching job details:', error);
        }
    }

    function getPriorityText(p) {
        const val = p.toString().toLowerCase();
        if (val === '0' || val === 'low') return 'Low';
        if (val === '1' || val === 'normal') return 'Normal';
        if (val === '2' || val === 'high') return 'High';
        if (val === '3' || val === 'critical') return 'Critical';
        return p;
    }

    function renderLogs(logs) {
        if (logs.length === 0) {
            detLogs.innerHTML = '<div class="no-logs">No execution logs written yet.</div>';
            return;
        }

        detLogs.innerHTML = '';
        logs.forEach(log => {
            const row = document.createElement('div');
            row.className = 'log-row';

            const time = new Date(log.timestamp).toLocaleTimeString();
            const levelClass = `level-${log.logLevel.toString().toLowerCase()}`;
            const levelText = log.logLevel;

            row.innerHTML = `
                <span class="log-time">[${time}]</span>
                <span class="log-level ${levelClass}">${levelText}:</span>
                <span class="log-msg">${log.message}</span>
            `;
            detLogs.appendChild(row);
        });

        // Auto-scroll to bottom of console
        detLogs.scrollTop = detLogs.scrollHeight;
    }

    // Event Listeners
    btnEnqueueSuccess.addEventListener('click', () => enqueueJob(false));
    btnEnqueueFail.addEventListener('click', () => enqueueJob(true));
    btnRefresh.addEventListener('click', updateDashboard);
    btnCloseDetails.addEventListener('click', () => {
        detailsPanel.classList.add('hidden');
        selectedJobId = null;
    });

    // Start Polling Loops
    updateDashboard();
    setInterval(updateDashboard, 2000); // refresh feed every 2 seconds

    // If a job is currently viewed, refresh its details too
    setInterval(() => {
        if (selectedJobId) {
            showJobDetails(selectedJobId);
        }
    }, 2000);
});
