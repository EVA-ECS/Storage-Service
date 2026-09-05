-- Nur in einer NEUEN, LEEREN lokalen Wegwerf-Datenbank ausführen, niemals in Supabase.
-- psql -v ON_ERROR_STOP=1 -f /database/Tests/receiver_id_migration_test.sql
\set ON_ERROR_STOP on

CREATE TABLE public.profiles (id uuid PRIMARY KEY);
CREATE TABLE public.rooms (id uuid PRIMARY KEY, is_group boolean NOT NULL);
CREATE TABLE public.room_members (
    room_id uuid REFERENCES public.rooms(id), user_id uuid REFERENCES public.profiles(id),
    PRIMARY KEY (room_id, user_id)
);
CREATE TABLE public.messages (
    id uuid PRIMARY KEY, room_id uuid NOT NULL REFERENCES public.rooms(id),
    sender_id uuid NOT NULL REFERENCES public.profiles(id), content text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE public.messages ENABLE ROW LEVEL SECURITY;

INSERT INTO public.profiles VALUES
    ('11111111-1111-1111-1111-111111111111'), ('22222222-2222-2222-2222-222222222222');
INSERT INTO public.rooms VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', false);
INSERT INTO public.room_members VALUES
    ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '11111111-1111-1111-1111-111111111111'),
    ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', '22222222-2222-2222-2222-222222222222');
INSERT INTO public.messages (id, room_id, sender_id, content) VALUES
    ('00000000-0000-0000-0000-000000000001', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
     '11111111-1111-1111-1111-111111111111', 'Altbestand');

\ir ../Migrations/20260905_add_receiver_id.sql
-- Erneutes Anwenden muss ohne Datenverlust funktionieren.
\ir ../Migrations/20260905_add_receiver_id.sql

-- In Phase 1 muss die bisherige Storage-Version während des Deployments noch schreiben können.
INSERT INTO public.messages (id, room_id, sender_id, content) VALUES
    ('00000000-0000-0000-0000-000000000006', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
     '11111111-1111-1111-1111-111111111111', 'waehrend Deployment');

-- Nach dem Storage-Deployment das Empfängerfeld für alle neuen/geänderten Zeilen verlangen.
\ir ../Migrations/20260906_require_receiver_id_for_new_messages.sql
\ir ../Migrations/20260906_require_receiver_id_for_new_messages.sql

INSERT INTO public.messages (id, room_id, sender_id, receiver_id, content) VALUES
    ('00000000-0000-0000-0000-000000000002', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
     '11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', 'A an B'),
    ('00000000-0000-0000-0000-000000000003', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
     '22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 'B an A');

-- Die mitgelieferte leere Freigabeliste mit ROLLBACK darf keine Altdaten ändern.
\ir ../Backfill/review_receiver_ids.sql

DO $test$
DECLARE
    current_user_id uuid;
    other_user_id uuid;
    visible_rows integer;
BEGIN
    IF (SELECT count(*) FROM public.messages) <> 4 THEN
        RAISE EXCEPTION 'Migration/Probelauf hat Nachrichten verloren oder hinzugefügt.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public.messages
                   WHERE content = 'Altbestand' AND receiver_id IS NULL) THEN
        RAISE EXCEPTION 'Altbestand wurde ungeprüft verändert.';
    END IF;
    IF NOT (SELECT relrowsecurity FROM pg_class WHERE oid = 'public.messages'::regclass) THEN
        RAISE EXCEPTION 'RLS wurde verändert.';
    END IF;
    IF to_regclass('public.messages_private_history_sent_idx') IS NULL
       OR to_regclass('public.messages_private_history_received_idx') IS NULL THEN
        RAISE EXCEPTION 'History-Indizes fehlen.';
    END IF;
    BEGIN
        INSERT INTO public.messages (id, room_id, sender_id, content) VALUES
            ('00000000-0000-0000-0000-000000000005', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
             '11111111-1111-1111-1111-111111111111', 'neue Nachricht ohne Empfaenger');
        RAISE EXCEPTION 'Neue Nachricht ohne receiver_id wurde gespeichert.';
    EXCEPTION WHEN check_violation THEN
        NULL; -- Erwartet: NOT-VALID-Check schützt neue/geänderte Zeilen, nicht den Altbestand.
    END;
    BEGIN
        INSERT INTO public.messages (id, room_id, sender_id, receiver_id, content) VALUES
            ('00000000-0000-0000-0000-000000000004', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
             '11111111-1111-1111-1111-111111111111', '99999999-9999-9999-9999-999999999999', 'ungueltig');
        RAISE EXCEPTION 'Unbekannter Empfänger wurde trotz Fremdschlüssel gespeichert.';
    EXCEPTION WHEN foreign_key_violation THEN
        NULL; -- Erwartet: receiver_id verweist auf profiles.id.
    END;

    -- Prüft nur die Filterlogik in beiden Richtungen, keine Benutzer-Anmeldung oder RLS-Policy.
    FOR current_user_id IN SELECT id FROM public.profiles LOOP
        SELECT id INTO STRICT other_user_id FROM public.profiles WHERE id <> current_user_id;
        SELECT count(*) INTO visible_rows FROM public.messages
        WHERE room_id = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
          AND ((sender_id = current_user_id AND receiver_id = other_user_id)
            OR (sender_id = other_user_id AND receiver_id = current_user_id));
        IF visible_rows <> 2 THEN
            RAISE EXCEPTION 'Beide Richtungen wurden für % nicht gefunden.', current_user_id;
        END IF;
    END LOOP;
END
$test$;

SELECT 'BESTANDEN: Beide Deploy-Phasen wiederholbar, Altbestand erhalten, Pflichtfeld/FK/Indizes/RLS-Status und History-Filter geprüft.' AS ergebnis;
