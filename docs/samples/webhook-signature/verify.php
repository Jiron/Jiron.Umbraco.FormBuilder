<?php

// Checks the X-Jfb-Signature header of a webhook from Jiron Form Builder (docs/actions.md).

// How far the header's time may be from now, in seconds, so a recorded request cannot be sent again later.
const JFB_TOLERANCE_SECONDS = 300;

/**
 * Whether the header is genuine for these bytes of the body, exactly as they arrived, and recent.
 *
 * @param string $secret The signing secret.
 * @param string|null $header The value of the X-Jfb-Signature header.
 * @param string $body The request's body, before any parsing: file_get_contents('php://input').
 * @param int|null $now The time now, in seconds since 1970.
 */
function jfb_is_genuine(string $secret, ?string $header, string $body, ?int $now = null): bool
{
    $time = null;
    $signature = null;
    foreach (explode(',', $header ?? '') as $part) {
        $part = trim($part);
        if (str_starts_with($part, 't=')) {
            if ($time !== null) {
                return false;
            }
            $time = substr($part, 2);
        } elseif (str_starts_with($part, 'v1=')) {
            if ($signature !== null) {
                return false;
            }
            $signature = substr($part, 3);
        }
    }

    if ($time === null || $signature === null || !ctype_digit($time)) {
        return false;
    }

    if (abs(($now ?? time()) - (int) $time) > JFB_TOLERANCE_SECONDS) {
        return false;
    }

    $expected = hash_hmac('sha256', $time . '.' . $body, $secret);
    return hash_equals($expected, $signature);
}

// From a command line: php verify.php <secret> <header> <file with the body> [seconds since 1970, now when left out];
// exit code 0 when genuine.
if (PHP_SAPI === 'cli' && realpath($argv[0]) === __FILE__) {
    exit(jfb_is_genuine($argv[1], $argv[2], file_get_contents($argv[3]), isset($argv[4]) ? (int) $argv[4] : null) ? 0 : 1);
}
