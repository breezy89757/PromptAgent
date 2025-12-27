// Chart.js Interop for PromptAgent
// Provides functions to create and destroy Chart.js charts

window.chartInterop = {
    charts: {},

    /**
     * Create a radar chart
     * @param {string} canvasId - Canvas element ID
     * @param {object} data - Chart data with labels and datasets
     */
    createRadarChart: function (canvasId, data) {
        this.destroyChart(canvasId);

        const ctx = document.getElementById(canvasId);
        if (!ctx) {
            console.error('Canvas not found:', canvasId);
            return;
        }

        this.charts[canvasId] = new Chart(ctx, {
            type: 'radar',
            data: {
                labels: data.labels,
                datasets: data.datasets.map((ds, index) => ({
                    label: ds.label,
                    data: ds.data,
                    fill: true,
                    backgroundColor: this.getColorWithAlpha(index, 0.2),
                    borderColor: this.getColor(index),
                    pointBackgroundColor: this.getColor(index),
                    pointBorderColor: '#fff',
                    pointHoverBackgroundColor: '#fff',
                    pointHoverBorderColor: this.getColor(index),
                    borderWidth: 2
                }))
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        position: 'bottom',
                        labels: {
                            color: '#aaa',
                            font: { size: 12 }
                        }
                    }
                },
                scales: {
                    r: {
                        beginAtZero: true,
                        max: 100,
                        ticks: {
                            stepSize: 20,
                            color: '#666',
                            backdropColor: 'transparent'
                        },
                        grid: {
                            color: 'rgba(255, 255, 255, 0.1)'
                        },
                        angleLines: {
                            color: 'rgba(255, 255, 255, 0.1)'
                        },
                        pointLabels: {
                            color: '#aaa',
                            font: { size: 11 }
                        }
                    }
                }
            }
        });
    },

    /**
     * Create a line chart for cost curves
     * @param {string} canvasId - Canvas element ID
     * @param {object} data - Chart data with labels and datasets
     */
    createLineChart: function (canvasId, data) {
        this.destroyChart(canvasId);

        const ctx = document.getElementById(canvasId);
        if (!ctx) {
            console.error('Canvas not found:', canvasId);
            return;
        }

        this.charts[canvasId] = new Chart(ctx, {
            type: 'line',
            data: {
                labels: data.labels,
                datasets: data.datasets.map((ds, index) => ({
                    label: ds.label,
                    data: ds.data,
                    fill: false,
                    borderColor: this.getColor(index),
                    backgroundColor: this.getColorWithAlpha(index, 0.5),
                    tension: 0.3,
                    borderWidth: 2,
                    pointRadius: 3,
                    pointHoverRadius: 5
                }))
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: {
                        position: 'bottom',
                        labels: {
                            color: '#aaa',
                            font: { size: 12 }
                        }
                    },
                    tooltip: {
                        callbacks: {
                            label: function (context) {
                                return context.dataset.label + ': NT$' + context.parsed.y.toLocaleString();
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        title: {
                            display: true,
                            text: '使用量 (次/月)',
                            color: '#aaa'
                        },
                        ticks: { color: '#888' },
                        grid: { color: 'rgba(255, 255, 255, 0.05)' }
                    },
                    y: {
                        title: {
                            display: true,
                            text: '成本 (USD)',
                            color: '#aaa'
                        },
                        ticks: {
                            color: '#888',
                            callback: function (value) {
                                return '$' + value.toLocaleString();
                            }
                        },
                        grid: { color: 'rgba(255, 255, 255, 0.05)' }
                    }
                }
            }
        });
    },

    /**
     * Destroy a chart instance
     * @param {string} canvasId - Canvas element ID
     */
    destroyChart: function (canvasId) {
        if (this.charts[canvasId]) {
            this.charts[canvasId].destroy();
            delete this.charts[canvasId];
        }
    },

    /**
     * Get color for dataset by index
     */
    getColor: function (index) {
        const colors = [
            '#667eea', // Purple - GAI
            '#10b981', // Green - Traditional
            '#f59e0b'  // Orange - Manual
        ];
        return colors[index % colors.length];
    },

    /**
     * Get color with alpha for dataset by index
     */
    getColorWithAlpha: function (index, alpha) {
        const colors = [
            `rgba(102, 126, 234, ${alpha})`,
            `rgba(16, 185, 129, ${alpha})`,
            `rgba(245, 158, 11, ${alpha})`
        ];
        return colors[index % colors.length];
    }
};
