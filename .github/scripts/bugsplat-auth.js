'use strict';
// Authenticates with BugSplat using OAuth2 client_credentials and writes a
// JSON object containing access_token to stdout when successful.
// Uses only Node.js built-in modules — no npm install required.
const https = require('https');

const { BUGSPLAT_CLIENT_ID, BUGSPLAT_CLIENT_SECRET } = process.env;
if (!BUGSPLAT_CLIENT_ID || !BUGSPLAT_CLIENT_SECRET) {
  process.stderr.write('BUGSPLAT_CLIENT_ID and BUGSPLAT_CLIENT_SECRET env vars must be set\n');
  process.exit(1);
}

function post(path, bodyObj) {
  return new Promise((resolve, reject) => {
    const body = new URLSearchParams(bodyObj).toString();
    const req = https.request(
      {
        hostname: 'app.bugsplat.com',
        path,
        method: 'POST',
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded',
          'Content-Length': Buffer.byteLength(body),
        },
      },
      (res) => {
        const parts = [];
        res.on('data', (c) => parts.push(c));
        res.on('end', () => {
          resolve({
            status: res.statusCode || 0,
            body: Buffer.concat(parts).toString(),
            path,
          });
        });
      }
    );

    req.on('error', reject);
    req.end(body);
  });
}

async function main() {
  const attempts = [
    {
      path: '/oauth2/token',
      body: {
        grant_type: 'client_credentials',
        client_id: BUGSPLAT_CLIENT_ID,
        client_secret: BUGSPLAT_CLIENT_SECRET,
      },
    },
    {
      path: '/oauth2/authorize',
      body: {
        grant_type: 'client_credentials',
        client_id: BUGSPLAT_CLIENT_ID,
        client_secret: BUGSPLAT_CLIENT_SECRET,
      },
    },
  ];

  let last = { status: 0, body: '', path: '' };
  for (const attempt of attempts) {
    const result = await post(attempt.path, attempt.body);
    last = result;

    if (result.body) {
      try {
        const obj = JSON.parse(result.body);
        if (obj && obj.access_token) {
          process.stdout.write(JSON.stringify(obj));
          return;
        }
      } catch {
        // Not JSON; continue to next attempt.
      }
    }
  }

  process.stderr.write(`BugSplat auth failed (last endpoint: ${last.path}, status: ${last.status}, body: ${last.body || '<empty>'})\n`);
  process.stdout.write(last.body || '');
}

main().catch((e) => {
  process.stderr.write((e && e.message ? e.message : String(e)) + '\n');
  process.exit(1);
});
