// Dev-server proxy: the browser talks only to the frontend origin (http://127.0.0.1:4200),
// /api/* is forwarded to CityFy.Api. Same origin = the session cookie set by the API
// (also by the Spotify callback /api/v1/auth/callback) belongs to the frontend origin.
//
// Under Aspire the API address comes from service discovery (WithReference(api)),
// otherwise the fixed launchSettings port of CityFy.Api is used.
const target =
  process.env['services__Api__https__0'] ??
  process.env['services__Api__http__0'] ??
  'https://localhost:7300';

export default {
  '/api': {
    target,
    secure: false, // local development certificate
    changeOrigin: false, // keep Host = 127.0.0.1:4200 so cookies/redirects stay on the frontend origin
    logLevel: 'warn',
  },
};
