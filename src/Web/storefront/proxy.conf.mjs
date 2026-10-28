// Dev-server proxy: forwards /api to the YARP gateway so the browser never needs CORS.
// Aspire injects GATEWAY_URL; running standalone falls back to the gateway's launch profile port.
const target = process.env.GATEWAY_URL ?? 'http://localhost:5100';

export default {
  '/api': { target, secure: false, changeOrigin: true },
};
