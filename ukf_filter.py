import math
import time


class UnscentedKalmanFilter1D:
    """
    1D Unscented Kalman Filter for grip force smoothing.
    Removes sensor noise & jitter for smooth, responsive force output.
    
    State: x = [force (N), d_force/dt (N/s)]^T
    Measurement: z = force (N)
    
    Pure Python implementation (no third-party deps).
    """
    def __init__(self, q_force=1.5, q_dforce=8.0, r_measure=0.35, alpha=0.2, beta=2.0, kappa=0.0):
        # State: [force (N), rate of change (N/s)]
        self.x = [0.0, 0.0]
        self.dim = 2

        # Covariance matrix P (2x2)
        self.P = [
            [1.0, 0.0],
            [0.0, 1.0]
        ]

        # Process noise parameters
        self.q_force = q_force
        self.q_dforce = q_dforce

        # Measurement noise covariance R
        self.R = r_measure

        # UKF Unscented Transform parameters
        self.alpha = alpha
        self.beta = beta
        self.kappa = kappa
        self.lmbda = (self.alpha ** 2) * (self.dim + self.kappa) - self.dim

        # Compute weights
        denom = self.dim + self.lmbda
        self.wm = [self.lmbda / denom] + [1.0 / (2.0 * denom)] * (2 * self.dim)
        self.wc = [self.lmbda / denom + (1.0 - self.alpha ** 2 + self.beta)] + [1.0 / (2.0 * denom)] * (2 * self.dim)

        self.last_time = None
        self.initialized = False

    def reset(self, initial_force=0.0):
        """ Reset filter state (e.g. after Tare or restart) """
        self.x = [max(0.0, float(initial_force)), 0.0]
        self.P = [[1.0, 0.0], [0.0, 1.0]]
        self.last_time = None
        self.initialized = True

    def _cholesky2x2(self, M):
        """ Cholesky decomposition for 2x2 positive semi-definite matrix """
        L00 = math.sqrt(max(1e-9, M[0][0]))
        L10 = M[1][0] / L00
        L11 = math.sqrt(max(1e-9, M[1][1] - L10 * L10))
        return [[L00, 0.0], [L10, L11]]

    def _generate_sigma_points(self, mean, cov):
        """ Generate 2n+1 = 5 sigma points around the mean """
        c = max(1e-9, self.dim + self.lmbda)
        scaled_cov = [
            [cov[0][0] * c, cov[0][1] * c],
            [cov[1][0] * c, cov[1][1] * c]
        ]
        L = self._cholesky2x2(scaled_cov)

        sigmas = [list(mean)]
        # + L columns
        sigmas.append([mean[0] + L[0][0], mean[1] + L[1][0]])
        sigmas.append([mean[0] + L[0][1], mean[1] + L[1][1]])
        # - L columns
        sigmas.append([mean[0] - L[0][0], mean[1] - L[1][0]])
        sigmas.append([mean[0] - L[0][1], mean[1] - L[1][1]])
        return sigmas

    def update(self, raw_force, current_time=None):
        """
        Filter raw force through UKF.
        Returns: smoothed force value free of sensor noise.
        """
        raw_val = max(0.0, float(raw_force))

        if not self.initialized:
            self.reset(raw_val)
            self.last_time = current_time or time.time()
            return raw_val

        # Compute delta time
        now = current_time or time.time()
        dt = 0.02
        if self.last_time is not None:
            dt = max(0.001, min(0.2, now - self.last_time))
        self.last_time = now

        # 1. Generate sigma points from current state
        sigmas = self._generate_sigma_points(self.x, self.P)

        # 2. Predict step: propagate sigma points through kinematic model
        sigmas_pred = []
        for s in sigmas:
            f_p = s[0] + s[1] * dt
            df_p = s[1]
            sigmas_pred.append([f_p, df_p])

        # Predicted state mean
        x_pred = [0.0, 0.0]
        for i in range(len(sigmas_pred)):
            x_pred[0] += self.wm[i] * sigmas_pred[i][0]
            x_pred[1] += self.wm[i] * sigmas_pred[i][1]

        # Predicted covariance + process noise Q
        P_pred = [[0.0, 0.0], [0.0, 0.0]]
        for i in range(len(sigmas_pred)):
            dx = [sigmas_pred[i][0] - x_pred[0], sigmas_pred[i][1] - x_pred[1]]
            w = self.wc[i]
            P_pred[0][0] += w * dx[0] * dx[0]
            P_pred[0][1] += w * dx[0] * dx[1]
            P_pred[1][0] += w * dx[1] * dx[0]
            P_pred[1][1] += w * dx[1] * dx[1]

        # Add process noise Q
        P_pred[0][0] += self.q_force * dt
        P_pred[1][1] += self.q_dforce * dt

        # 3. Measurement update step: h(x) = x[0]
        z_sigmas = [s[0] for s in sigmas_pred]

        # Predicted measurement mean
        z_pred = 0.0
        for i in range(len(z_sigmas)):
            z_pred += self.wm[i] * z_sigmas[i]

        # Innovation covariance S
        S = self.R
        for i in range(len(z_sigmas)):
            dz = z_sigmas[i] - z_pred
            S += self.wc[i] * dz * dz

        # Cross covariance Pxz
        Pxz = [0.0, 0.0]
        for i in range(len(sigmas_pred)):
            dx = [sigmas_pred[i][0] - x_pred[0], sigmas_pred[i][1] - x_pred[1]]
            dz = z_sigmas[i] - z_pred
            w = self.wc[i]
            Pxz[0] += w * dx[0] * dz
            Pxz[1] += w * dx[1] * dz

        # Kalman gain K = Pxz / S
        K = [Pxz[0] / S, Pxz[1] / S]

        # Innovation
        y = raw_val - z_pred

        # Update state: x = x_pred + K * y
        self.x[0] = x_pred[0] + K[0] * y
        self.x[1] = x_pred[1] + K[1] * y

        # Physical constraint: force cannot be negative
        if self.x[0] < 0.0:
            self.x[0] = 0.0
            self.x[1] = 0.0

        # Update covariance: P = P_pred - K * S * K^T
        self.P[0][0] = P_pred[0][0] - K[0] * S * K[0]
        self.P[0][1] = P_pred[0][1] - K[0] * S * K[1]
        self.P[1][0] = P_pred[1][0] - K[1] * S * K[0]
        self.P[1][1] = P_pred[1][1] - K[1] * S * K[1]

        return self.x[0]

    @property
    def current_force(self):
        """ Filtered force value (N) """
        return self.x[0]

    @property
    def force_rate(self):
        """ Rate of force change (N/s) """
        return self.x[1]
