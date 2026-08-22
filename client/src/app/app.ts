import { Component, inject, OnInit, signal } from '@angular/core';
import { HealthService } from './health.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly healthService = inject(HealthService);

  protected readonly healthStatus = signal('verificando...');
  protected readonly apiOnline = signal<boolean | null>(null);

  ngOnInit(): void {
    this.healthService.check().subscribe({
      next: ({ status }) => {
        this.healthStatus.set(status);
        this.apiOnline.set(true);
      },
      error: () => {
        this.healthStatus.set('indisponível');
        this.apiOnline.set(false);
      },
    });
  }
}
