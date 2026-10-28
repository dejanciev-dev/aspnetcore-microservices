// Dev-server proxy: forwards API, BFF and OIDC callback paths to the YARP gateway, so the session
// cookie is first-party and the browser never needs CORS. changeOrigin stays false: the gateway
// must see Host "localhost:4200" to build redirect URIs that Keycloak accepts.
// Aspire injects GATEWAY_URL; running standalone falls back to the gateway's launch profile port.
const target = process.env.GATEWAY_URL ?? 'http://localhost:5100';
const toGateway = { target, secure: false, changeOrigin: false, xfwd: true };

export default {
  '/api': toGateway,
  '/bff': toGateway,
  '/signin-oidc': toGateway,
  '/signout-callback-oidc': toGateway,
};
