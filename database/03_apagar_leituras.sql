-- =============================================================================
-- Apaga leituras e resumos, mantendo os cadastros (máquinas, atributos, status).
-- Use depois de testar com o simulador, para não misturar dados fictícios com
-- os reais. Pare o backend antes:
--     mysql -u root -p pi3 < database/03_apagar_leituras.sql
-- =============================================================================

DELETE FROM valor;
DELETE FROM leitura;
DELETE FROM resumo_atributo;
DELETE FROM resumo_status;
UPDATE sintese_controle SET processado_ate = NULL;
UPDATE maquina SET status_atual_id = NULL, status_desde = NULL, ultima_leitura = NULL;
