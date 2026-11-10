-- =============================================================================
-- Projeto Integrador III — modelo físico (protótipo, versão 1)
--
-- Aplicado automaticamente pelo backend a cada inicialização. É idempotente:
-- só cria o que não existe e só insere os cadastros iniciais que faltam.
-- Durante o protótipo, mudanças de estrutura podem exigir recriar o banco
-- (DROP DATABASE pi3; e rodar o 01 de novo).
--
-- Convenções:
--   * Todo horário é gravado em UTC. Os limites de dia/semana/mês/ano seguem o
--     horário de Brasília (UTC-3), calculados pelo backend.
--   * Cadastros (dimensões) usam ids pequenos e densos: o backend os carrega
--     em memória como arrays indexados pelo id (Data Oriented Design).
--   * Tabelas de fatos são ordenadas fisicamente por instante (chave primária
--     começando pelo tempo): inserção sempre no fim, leitura e limpeza por
--     faixa de tempo contínua.
-- =============================================================================

-- ----------------------------------------------------------------------------
-- Cadastros (dimensões)
-- ----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS setor (
  id    TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  nome  VARCHAR(60)      NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_setor_nome (nome)
) ENGINE = InnoDB;

CREATE TABLE IF NOT EXISTS tipo_maquina (
  id      SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo  VARCHAR(60)       NOT NULL,   -- como vem da API (campo "tipo")
  nome    VARCHAR(100)      NOT NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_tipo_maquina_codigo (codigo)
) ENGINE = InnoDB;

-- Status possíveis. "categoria" agrupa os códigos da API nos três estados do
-- negócio (operação, manutenção, parada); "alerta" marca estados de atenção.
CREATE TABLE IF NOT EXISTS status_maquina (
  id         TINYINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo     VARCHAR(40)      NOT NULL,
  nome       VARCHAR(60)      NOT NULL,
  categoria  ENUM('OPERACAO', 'MANUTENCAO', 'PARADA', 'DESCONHECIDO') NOT NULL,
  alerta     BOOLEAN          NOT NULL DEFAULT FALSE,
  PRIMARY KEY (id),
  UNIQUE KEY uq_status_maquina_codigo (codigo)
) ENGINE = InnoDB;

-- MEDIDA: valor instantâneo (temperatura). CONTADOR: valor acumulado que só
-- cresce (produção); nos resumos vale a variação no período.
CREATE TABLE IF NOT EXISTS atributo (
  id        SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo    VARCHAR(60)       NOT NULL,   -- nome do campo na API
  nome      VARCHAR(100)      NOT NULL,
  unidade   VARCHAR(20)       NULL,       -- NULL = unidade não confirmada
  natureza  ENUM('MEDIDA', 'CONTADOR') NOT NULL DEFAULT 'MEDIDA',
  PRIMARY KEY (id),
  UNIQUE KEY uq_atributo_codigo (codigo)
) ENGINE = InnoDB;

CREATE TABLE IF NOT EXISTS maquina (
  id               SMALLINT UNSIGNED NOT NULL AUTO_INCREMENT,
  codigo           VARCHAR(60)       NOT NULL,   -- campo "maquina" da API
  tipo_id          SMALLINT UNSIGNED NOT NULL,
  setor_id         TINYINT UNSIGNED  NOT NULL,
  status_atual_id  TINYINT UNSIGNED  NULL,
  status_desde     DATETIME(3)       NULL,
  ultima_leitura   DATETIME(3)       NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uq_maquina_codigo (codigo),
  CONSTRAINT fk_maquina_tipo   FOREIGN KEY (tipo_id)         REFERENCES tipo_maquina (id),
  CONSTRAINT fk_maquina_setor  FOREIGN KEY (setor_id)        REFERENCES setor (id),
  CONSTRAINT fk_maquina_status FOREIGN KEY (status_atual_id) REFERENCES status_maquina (id)
) ENGINE = InnoDB;

-- Quais atributos cada tipo de máquina envia (aprendido das leituras) e, no
-- futuro, os limites operacionais de cada um.
CREATE TABLE IF NOT EXISTS atributo_tipo_maquina (
  tipo_id      SMALLINT UNSIGNED NOT NULL,
  atributo_id  SMALLINT UNSIGNED NOT NULL,
  limite_min   DOUBLE            NULL,
  limite_max   DOUBLE            NULL,
  PRIMARY KEY (tipo_id, atributo_id),
  CONSTRAINT fk_atm_tipo     FOREIGN KEY (tipo_id)     REFERENCES tipo_maquina (id),
  CONSTRAINT fk_atm_atributo FOREIGN KEY (atributo_id) REFERENCES atributo (id)
) ENGINE = InnoDB;

CREATE TABLE IF NOT EXISTS granularidade (
  id    TINYINT UNSIGNED NOT NULL,
  nome  VARCHAR(20)      NOT NULL,
  PRIMARY KEY (id)
) ENGINE = InnoDB;

-- ----------------------------------------------------------------------------
-- Fatos brutos (tempo real) — guardados por poucos dias e depois sintetizados
-- ----------------------------------------------------------------------------

-- Uma linha por leitura recebida da API.
CREATE TABLE IF NOT EXISTS leitura (
  instante     DATETIME(3)       NOT NULL,   -- "timestamp" da API (UTC)
  maquina_id   SMALLINT UNSIGNED NOT NULL,
  status_id    TINYINT UNSIGNED  NOT NULL,
  id_api       INT UNSIGNED      NULL,       -- "id" da API (reinicia quando a API reinicia)
  duracao_ms   INT UNSIGNED      NULL,       -- tempo desde a leitura anterior; NULL = primeira ou após lacuna
  coletado_em  DATETIME(3)       NOT NULL,   -- relógio do coletor (UTC), só para auditoria
  qualidade    SMALLINT UNSIGNED NOT NULL DEFAULT 0,  -- bits de tratativa (docs/tratativas-de-dados.md)
  PRIMARY KEY (instante, maquina_id),
  CONSTRAINT fk_leitura_maquina FOREIGN KEY (maquina_id) REFERENCES maquina (id),
  CONSTRAINT fk_leitura_status  FOREIGN KEY (status_id)  REFERENCES status_maquina (id)
) ENGINE = InnoDB;

-- Valores numéricos de cada leitura (modelo atributo-valor do DER original).
CREATE TABLE IF NOT EXISTS valor (
  instante     DATETIME(3)       NOT NULL,
  maquina_id   SMALLINT UNSIGNED NOT NULL,
  atributo_id  SMALLINT UNSIGNED NOT NULL,
  valor        DOUBLE            NOT NULL,
  incremento   DOUBLE            NULL,       -- só CONTADOR: variação desde a leitura anterior
  PRIMARY KEY (instante, maquina_id, atributo_id),
  CONSTRAINT fk_valor_leitura  FOREIGN KEY (instante, maquina_id) REFERENCES leitura (instante, maquina_id),
  CONSTRAINT fk_valor_atributo FOREIGN KEY (atributo_id)          REFERENCES atributo (id)
) ENGINE = InnoDB;

-- ----------------------------------------------------------------------------
-- Resumos (substituem Historico_Maquina e Relatorio_Semanal_Maquina)
-- Mesma estrutura em todas as granularidades: hora -> dia -> semana/mês -> ano.
-- Os campos são "somáveis", então um nível é calculado exatamente a partir do
-- nível abaixo, sem precisar dos dados brutos.
-- ----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS resumo_atributo (
  granularidade   TINYINT UNSIGNED  NOT NULL,
  inicio          DATETIME          NOT NULL,   -- início do período (UTC)
  maquina_id      SMALLINT UNSIGNED NOT NULL,
  atributo_id     SMALLINT UNSIGNED NOT NULL,
  n               INT UNSIGNED      NOT NULL,   -- quantidade de leituras
  soma            DOUBLE            NOT NULL,
  soma_quadrados  DOUBLE            NOT NULL,   -- para desvio padrão
  minimo          DOUBLE            NOT NULL,
  maximo          DOUBLE            NOT NULL,
  primeiro        DOUBLE            NOT NULL,   -- primeiro valor do período
  ultimo          DOUBLE            NOT NULL,   -- último valor do período
  incremento      DOUBLE            NULL,       -- só CONTADOR: soma das variações
  PRIMARY KEY (granularidade, inicio, maquina_id, atributo_id),
  CONSTRAINT fk_ra_granularidade FOREIGN KEY (granularidade) REFERENCES granularidade (id),
  CONSTRAINT fk_ra_maquina       FOREIGN KEY (maquina_id)    REFERENCES maquina (id),
  CONSTRAINT fk_ra_atributo      FOREIGN KEY (atributo_id)   REFERENCES atributo (id)
) ENGINE = InnoDB;

-- Tempo que cada máquina passou em cada status, por período.
CREATE TABLE IF NOT EXISTS resumo_status (
  granularidade  TINYINT UNSIGNED  NOT NULL,
  inicio         DATETIME          NOT NULL,
  maquina_id     SMALLINT UNSIGNED NOT NULL,
  status_id      TINYINT UNSIGNED  NOT NULL,
  segundos       DOUBLE            NOT NULL,
  leituras       INT UNSIGNED      NOT NULL,
  PRIMARY KEY (granularidade, inicio, maquina_id, status_id),
  CONSTRAINT fk_rs_granularidade FOREIGN KEY (granularidade) REFERENCES granularidade (id),
  CONSTRAINT fk_rs_maquina       FOREIGN KEY (maquina_id)    REFERENCES maquina (id),
  CONSTRAINT fk_rs_status        FOREIGN KEY (status_id)     REFERENCES status_maquina (id)
) ENGINE = InnoDB;

-- Até onde cada granularidade já foi fechada (períodos anteriores a
-- "processado_ate" não mudam mais e podem ter a fonte apagada pela retenção).
CREATE TABLE IF NOT EXISTS sintese_controle (
  granularidade   TINYINT UNSIGNED NOT NULL,
  processado_ate  DATETIME         NULL,
  PRIMARY KEY (granularidade),
  CONSTRAINT fk_sc_granularidade FOREIGN KEY (granularidade) REFERENCES granularidade (id)
) ENGINE = InnoDB;

-- ----------------------------------------------------------------------------
-- Cadastros iniciais (ids explícitos: reexecutar não consome auto_increment)
-- ----------------------------------------------------------------------------

INSERT IGNORE INTO setor (id, nome) VALUES (1, 'Setor1');

INSERT IGNORE INTO granularidade (id, nome) VALUES
  (1, 'HORA'), (2, 'DIA'), (3, 'SEMANA'), (4, 'MES'), (5, 'ANO');

INSERT IGNORE INTO sintese_controle (granularidade, processado_ate) VALUES
  (1, NULL), (2, NULL), (3, NULL), (4, NULL), (5, NULL);

INSERT IGNORE INTO status_maquina (id, codigo, nome, categoria, alerta) VALUES
  (1, 'OPERANDO',      'Em operação',              'OPERACAO',     FALSE),
  (2, 'ATENCAO',       'Em operação com atenção',  'OPERACAO',     TRUE),
  (3, 'MANUTENCAO',    'Em manutenção',            'MANUTENCAO',   FALSE),
  (4, 'EM_MANUTENCAO', 'Em manutenção',            'MANUTENCAO',   FALSE),
  (5, 'PARADA',        'Parada',                   'PARADA',       FALSE),
  (6, 'PARADO',        'Parada',                   'PARADA',       FALSE),
  (7, 'FALHA',         'Parada por falha',         'PARADA',       TRUE),
  (8, 'DESCONHECIDO',  'Status não informado',     'DESCONHECIDO', FALSE);

INSERT IGNORE INTO tipo_maquina (id, codigo, nome) VALUES
  (1, 'TORNO_CNC',              'Torno CNC'),
  (2, 'PRENSA_HIDRAULICA',      'Prensa hidráulica'),
  (3, 'COMPRESSOR_INDUSTRIAL',  'Compressor industrial'),
  (4, 'ESTEIRA_TRANSPORTADORA', 'Esteira transportadora'),
  (5, 'ROBO_SOLDAGEM',          'Robô de soldagem'),
  (6, 'EMBALADORA',             'Embaladora');

-- Unidades só onde o próprio nome do campo indica (consumo_kw, horas_operacao).
INSERT IGNORE INTO atributo (id, codigo, nome, unidade, natureza) VALUES
  ( 1, 'temperatura',    'Temperatura',        NULL, 'MEDIDA'),
  ( 2, 'vibracao',       'Vibração',           NULL, 'MEDIDA'),
  ( 3, 'rotacao',        'Rotação',            NULL, 'MEDIDA'),
  ( 4, 'pressao',        'Pressão',            NULL, 'MEDIDA'),
  ( 5, 'consumo_kw',     'Consumo de energia', 'kW', 'MEDIDA'),
  ( 6, 'velocidade',     'Velocidade',         NULL, 'MEDIDA'),
  ( 7, 'carga',          'Carga',              NULL, 'MEDIDA'),
  ( 8, 'corrente',       'Corrente',           NULL, 'MEDIDA'),
  ( 9, 'producao',       'Produção',           NULL, 'CONTADOR'),
  (10, 'defeitos',       'Defeitos',           NULL, 'CONTADOR'),
  (11, 'rejeitados',     'Rejeitados',         NULL, 'CONTADOR'),
  (12, 'ciclos',         'Ciclos',             NULL, 'CONTADOR'),
  (13, 'desgaste',       'Desgaste',           NULL, 'CONTADOR'),
  (14, 'horas_operacao', 'Horas de operação',  'h',  'CONTADOR'),
  (15, 'falhas',         'Falhas',             NULL, 'CONTADOR'),
  (16, 'paradas',        'Paradas',            NULL, 'CONTADOR');
