import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, dismissInstallResult, getInstallStatus, startInstall } from './api';
import { useI18n } from './i18n';
import type { InstallStatus, UpdateInfo } from './types';

const BUSY = ['downloading', 'verifying', 'extracting', 'restarting'];

interface Props {
  update: UpdateInfo | null;
}

/**
 * «Aggiorna» nelle Impostazioni (solo nell'app desktop): conferma, poi stato e progresso di scarico, verifica e riavvio.
 * Senza app desktop (server autonomo, browser) il server dice `supported: false` e non si mostra nulla.
 */
export function InstallUpdate({ update }: Props) {
  const { t, errorText } = useI18n();
  const [status, setStatus] = useState<InstallStatus | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [requestError, setRequestError] = useState<string | null>(null);
  // Il server si ferma per riavviarsi: da lì in poi non risponde più, ed è il segno che l'aggiornamento prosegue.
  const [connectionLost, setConnectionLost] = useState(false);
  const polling = useRef<number | null>(null);

  const refresh = useCallback(async () => {
    try {
      setStatus(await getInstallStatus());
    } catch {
      setConnectionLost(true);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const busy = status !== null && BUSY.includes(status.state);
  useEffect(() => {
    if (!busy) return;
    polling.current = window.setInterval(() => void refresh(), 700);
    return () => {
      if (polling.current !== null) window.clearInterval(polling.current);
    };
  }, [busy, refresh]);

  const version = update?.latest ?? null;

  async function install() {
    if (!version) return;
    setConfirming(false);
    setRequestError(null);
    try {
      setStatus(await startInstall(version));
    } catch (e) {
      setRequestError(errorText(e instanceof ApiError ? e.info : { code: 'unknown' }));
    }
  }

  async function dismissResult() {
    try {
      await dismissInstallResult();
    } catch {
      // se non si riesce, l'esito resta e si riprova dopo
    }
    await refresh();
  }

  if (!status || (status.reason === 'update-desktop-only' && !status.lastResult)) return null;

  const last = status.lastResult;
  const lastText = last
    ? last.ok
      ? t('update.last.ok', { version: last.version })
      : last.result === 'Broken'
        ? t('update.last.broken', { version: last.version })
        : last.result === 'NotChanged'
          ? t('update.last.notChanged', { version: last.version })
          : t('update.last.restored', { version: last.version })
    : null;

  // Niente da dire: né un esito passato, né una versione da offrire, né un aggiornamento in corso.
  const offering = Boolean(update?.available && version);
  if (!lastText && !offering && !busy && !connectionLost && status.state !== 'failed') return null;

  return (
    <div className="setting">
      <div className="setting-text">
        {lastText && (
          <p className={last?.ok ? 'muted small' : 'warn-text small'} role="status">
            {lastText}{' '}
            <button className="link" onClick={() => void dismissResult()}>
              {t('update.dismiss')}
            </button>
          </p>
        )}

        {update?.available && version && !status.supported && status.reason && (
          <p className="muted small">{t('update.cannot', { reason: errorText({ code: status.reason }) })}</p>
        )}

        {busy || connectionLost ? (
          <div role="status">
            <p className="small">
              {connectionLost || status.state === 'restarting'
                ? t('update.state.restarting')
                : status.state === 'downloading'
                  ? t('update.state.downloading', { percent: status.percent ?? 0 })
                  : t(`update.state.${status.state}` as 'update.state.verifying')}
            </p>
            {status.state === 'downloading' && !connectionLost && (
              <progress max={100} value={status.percent ?? 0} aria-label={t('update.state.downloading', { percent: status.percent ?? 0 })} />
            )}
          </div>
        ) : (
          <>
            {status.state === 'failed' && status.error && (
              <p className="warn-text small" role="alert">
                {t('update.failed', { reason: errorText(status.error) })}
              </p>
            )}
            {requestError && (
              <p className="warn-text small" role="alert">
                {requestError}
              </p>
            )}
            {confirming && version && (
              <div role="alertdialog" aria-labelledby="update-confirm" className="update-box">
                <p id="update-confirm" className="small">
                  {t('update.confirm', { version })}
                </p>
                <p className="muted small">{t('update.confirmNote')}</p>
                <div className="update-actions">
                  <button className="btn primary" onClick={() => void install()}>
                    {t('update.confirmYes')}
                  </button>
                  <button className="btn" onClick={() => setConfirming(false)}>
                    {t('update.confirmNo')}
                  </button>
                </div>
              </div>
            )}
          </>
        )}
      </div>
      {!busy && !connectionLost && !confirming && update?.available && version && status.supported && (
        <button className="btn primary" onClick={() => setConfirming(true)}>
          {t('update.install', { version })}
        </button>
      )}
    </div>
  );
}
