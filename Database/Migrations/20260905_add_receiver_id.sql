-- Vor dem aktualisierten Storage-Service ausführen. Keine vorhandenen Nachrichten löschen/umschreiben.
-- Nullable während der Umstellung: alte und Gruppen-Nachrichten haben ggf. keinen eindeutigen Empfänger.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';

ALTER TABLE public.messages ADD COLUMN IF NOT EXISTS receiver_id uuid;

DO $migration$
BEGIN
    IF (SELECT atttypid FROM pg_attribute
        WHERE attrelid = 'public.messages'::regclass AND attname = 'receiver_id' AND NOT attisdropped)
        <> 'uuid'::regtype THEN
        RAISE EXCEPTION 'messages.receiver_id existiert, ist aber nicht uuid. Schema manuell prüfen.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conrelid = 'public.messages'::regclass AND conname = 'messages_receiver_id_fkey') THEN
        ALTER TABLE public.messages ADD CONSTRAINT messages_receiver_id_fkey
            FOREIGN KEY (receiver_id) REFERENCES public.profiles(id) NOT VALID;
    END IF;

END
$migration$;

ALTER TABLE public.messages VALIDATE CONSTRAINT messages_receiver_id_fkey;

-- Unterstützt beide OR-Zweige einer privaten History-Abfrage (A,B) und (B,A).
CREATE INDEX IF NOT EXISTS messages_private_history_sent_idx
    ON public.messages (room_id, sender_id, receiver_id, created_at, id);
CREATE INDEX IF NOT EXISTS messages_private_history_received_idx
    ON public.messages (room_id, receiver_id, sender_id, created_at, id);

NOTIFY pgrst, 'reload schema';
COMMIT;
