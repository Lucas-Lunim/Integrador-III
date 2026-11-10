-- =============================================================================
-- Projeto Integrador III — criação do banco e do usuário do sistema
--
-- Rodar UMA vez, como root do MySQL:
--     mysql -u root -p < database/01_criar_banco_e_usuario.local.sql
--
-- Este arquivo é o modelo versionado. A cópia ".local.sql" (fora do Git) tem a
-- senha real gerada para esta máquina, a mesma do backend/Pi3.Backend/appsettings.Local.json.
-- As tabelas NÃO são criadas aqui: o backend aplica o database/02_schema.sql
-- sozinho ao iniciar.
-- =============================================================================

CREATE DATABASE IF NOT EXISTS pi3
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;

-- O backend conecta por TCP em 127.0.0.1; dependendo da resolução de nomes o
-- MySQL identifica a origem como 'localhost' ou '127.0.0.1', então criamos os dois.
CREATE USER IF NOT EXISTS 'pi3_app'@'localhost' IDENTIFIED BY '__SENHA__';
CREATE USER IF NOT EXISTS 'pi3_app'@'127.0.0.1' IDENTIFIED BY '__SENHA__';

GRANT ALL PRIVILEGES ON pi3.* TO 'pi3_app'@'localhost';
GRANT ALL PRIVILEGES ON pi3.* TO 'pi3_app'@'127.0.0.1';
