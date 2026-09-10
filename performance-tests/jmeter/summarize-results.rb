# frozen_string_literal: true

require "csv"
require "time"

result_file, report_file, target, threads, rampup, duration, loops = ARGV
abort "Usage: ruby summarize-results.rb RESULTS.jtl REPORT.md TARGET THREADS RAMPUP DURATION LOOPS" unless loops

rows = CSV.read(result_file, headers: true)
http_rows = rows.select { |row| row["URL"] && row["URL"] != "null" }
transaction_rows = rows.reject { |row| row["URL"] && row["URL"] != "null" }

def percentile(values, percentage)
  return nil if values.empty?

  sorted = values.sort
  sorted[[(percentage * sorted.length).ceil - 1, 0].max]
end

def metrics(rows)
  elapsed = rows.map { |row| row["elapsed"].to_f }
  passed = rows.count { |row| row["success"] == "true" }
  failed = rows.length - passed
  start_ms = rows.map { |row| row["timeStamp"].to_f }.min
  end_ms = rows.map { |row| row["timeStamp"].to_f + row["elapsed"].to_f }.max
  span_seconds = start_ms && end_ms ? [(end_ms - start_ms) / 1000.0, 0.001].max : 0

  {
    samples: rows.length,
    passed: passed,
    failed: failed,
    error_pct: rows.empty? ? 0 : failed * 100.0 / rows.length,
    average: elapsed.empty? ? nil : elapsed.sum / elapsed.length,
    median: percentile(elapsed, 0.50),
    min: elapsed.min,
    max: elapsed.max,
    p90: percentile(elapsed, 0.90),
    p95: percentile(elapsed, 0.95),
    p99: percentile(elapsed, 0.99),
    throughput: span_seconds.zero? ? 0 : rows.length / span_seconds,
    received_kb_sec: span_seconds.zero? ? 0 : rows.sum { |row| row["bytes"].to_f } / 1024.0 / span_seconds,
    sent_kb_sec: span_seconds.zero? ? 0 : rows.sum { |row| row["sentBytes"].to_f } / 1024.0 / span_seconds
  }
end

def value(number, decimals = 0)
  number.nil? ? "N/A" : format("%.#{decimals}f", number)
end

overall = metrics(http_rows)
result = if http_rows.empty?
           "BLOCKED — no HTTP samples were recorded"
         elsif overall[:failed].positive?
           "FAIL — one or more HTTP requests failed"
         else
           "PASS — all HTTP assertions passed"
         end

lines = []
lines << "# ResearchTrack JMeter Test Report"
lines << ""
lines << "- Generated: #{Time.now.iso8601}"
lines << "- Target: `#{target}`"
lines << "- Result file: `#{result_file}`"
lines << "- Threads: #{threads}"
lines << "- Ramp-up: #{rampup} seconds"
lines << "- Duration cap: #{duration} seconds"
lines << "- Loops: #{loops}"
lines << "- Result: **#{result}**"
lines << ""
lines << "## Overall HTTP Results"
lines << ""
lines << "| Samples | Passed | Failed | Error % | Average | Median | Min | Max | P90 | P95 | P99 | Requests/sec | Received KB/sec | Sent KB/sec |"
lines << "|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"
lines << "| #{overall[:samples]} | #{overall[:passed]} | #{overall[:failed]} | #{value(overall[:error_pct], 2)} | #{value(overall[:average])} ms | #{value(overall[:median])} ms | #{value(overall[:min])} ms | #{value(overall[:max])} ms | #{value(overall[:p90])} ms | #{value(overall[:p95])} ms | #{value(overall[:p99])} ms | #{value(overall[:throughput], 3)} | #{value(overall[:received_kb_sec], 3)} | #{value(overall[:sent_kb_sec], 3)} |"
lines << ""
lines << "## Endpoint Results"
lines << ""
lines << "| Label | Samples | Passed | Failed | Error % | Average | Min | Max | P90 | P95 | P99 | Requests/sec |"
lines << "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"
http_rows.group_by { |row| row["label"] }.each do |label, label_rows|
  item = metrics(label_rows)
  lines << "| `#{label}` | #{item[:samples]} | #{item[:passed]} | #{item[:failed]} | #{value(item[:error_pct], 2)} | #{value(item[:average])} ms | #{value(item[:min])} ms | #{value(item[:max])} ms | #{value(item[:p90])} ms | #{value(item[:p95])} ms | #{value(item[:p99])} ms | #{value(item[:throughput], 3)} |"
end

unless transaction_rows.empty?
  lines << ""
  lines << "## Transaction Results"
  lines << ""
  lines << "| Transaction | Samples | Passed | Failed | Error % | Average |"
  lines << "|---|---:|---:|---:|---:|---:|"
  transaction_rows.group_by { |row| row["label"] }.each do |label, label_rows|
    item = metrics(label_rows)
    lines << "| `#{label}` | #{item[:samples]} | #{item[:passed]} | #{item[:failed]} | #{value(item[:error_pct], 2)} | #{value(item[:average])} ms |"
  end
end

failures = http_rows.reject { |row| row["success"] == "true" }
unless failures.empty?
  lines << ""
  lines << "## Failed Requests"
  lines << ""
  lines << "| Label | Status | Response | Assertion/Error |"
  lines << "|---|---:|---|---|"
  failures.each do |row|
    failure = (row["failureMessage"] || "").gsub(/\s+/, " ").strip.gsub("|", "\\|")
    lines << "| `#{row['label']}` | #{row['responseCode']} | #{row['responseMessage']} | #{failure.empty? ? 'N/A' : failure} |"
  end
end

lines << ""
lines << "> No formal project performance acceptance threshold was found. PASS/FAIL above reflects HTTP and assertion success only."
lines << ""

File.write(report_file, lines.join("\n"))

puts
puts "================================================="
puts "RESEARCHTRACK JMETER TEST REPORT"
puts "================================================="
puts "Target:            #{target}"
puts "HTTP Samples:      #{overall[:samples]}"
puts "Passed:            #{overall[:passed]}"
puts "Failed:            #{overall[:failed]}"
puts "Error Rate:        #{value(overall[:error_pct], 2)}%"
puts "Average:           #{value(overall[:average])} ms"
puts "Median:            #{value(overall[:median])} ms"
puts "P90 / P95 / P99:  #{value(overall[:p90])} / #{value(overall[:p95])} / #{value(overall[:p99])} ms"
puts "Throughput:        #{value(overall[:throughput], 3)} requests/sec"
puts "Overall Result:    #{result}"
puts "Markdown Report:   #{report_file}"
puts "Raw Results:       #{result_file}"
puts "================================================="
